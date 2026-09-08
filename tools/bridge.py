"""Client-side driver for the Angband JSON bridge front end.

Used by the smoke tests and as the reference implementation of the protocol.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any, Iterator

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_GAME_DIR = REPO_ROOT / "engine" / "build" / "game"


class BridgeError(RuntimeError):
    pass


class Bridge:
    """Drives an Angband process running the `bridge` front end."""

    def __init__(self, game_dir: Path | None = None, savefile: str | None = None,
                 new_character: bool = True, timeout: float = 30.0):
        self.game_dir = Path(game_dir or DEFAULT_GAME_DIR)
        exe = self.game_dir / "angband.exe"
        if not exe.exists():
            exe = self.game_dir / "angband"
        if not exe.exists():
            raise BridgeError(f"no angband executable under {self.game_dir}")

        argv = [str(exe), "-mbridge"]
        if savefile:
            argv.append(f"-u{savefile}")
        if new_character:
            argv.append("-n")

        # Angband writes its own diagnostics to stderr; keep stdout pure JSON.
        self.proc = subprocess.Popen(
            argv,
            cwd=str(self.game_dir),
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            encoding="utf-8",
            bufsize=1,
        )
        self.timeout = timeout
        self.frame: dict[str, Any] | None = None
        self.hello = self._read()
        if self.hello.get("t") != "hello":
            raise BridgeError(f"expected hello, got {self.hello!r}")

        # The bridge emits a frame as soon as the game first blocks for input,
        # before any command is sent. Consume it so seq tracking stays aligned.
        self.next_frame()

    # -- plumbing ---------------------------------------------------------

    def _read(self) -> dict[str, Any]:
        line = self.proc.stdout.readline()
        if not line:
            raise BridgeError("bridge closed the connection")
        try:
            return json.loads(line)
        except json.JSONDecodeError as exc:
            raise BridgeError(f"invalid JSON from bridge: {line[:200]!r}") from exc

    def _write(self, line: str) -> None:
        if self.proc.poll() is not None:
            raise BridgeError("bridge process has exited")
        self.proc.stdin.write(line + "\n")
        self.proc.stdin.flush()

    def next_frame(self, after_seq: int = -1) -> dict[str, Any]:
        """Read until a frame newer than ``after_seq`` arrives."""
        while True:
            msg = self._read()
            if msg.get("t") == "frame":
                if msg.get("seq", 0) > after_seq:
                    self.frame = msg
                    return msg
                continue
            if msg.get("t") == "bye":
                raise BridgeError("bridge said goodbye")

    @property
    def seq(self) -> int:
        return self.frame.get("seq", 0) if self.frame else 0

    # -- commands ---------------------------------------------------------

    def key(self, spec: str) -> dict[str, Any]:
        before = self.seq
        self._write(f"key {spec}")
        return self.next_frame(before)

    def keys(self, text: str) -> dict[str, Any]:
        before = self.seq
        self._write(f"keys {text}")
        return self.next_frame(before)

    def set_map(self, enabled: bool) -> None:
        self._write("map " + ("on" if enabled else "off"))

    def quit(self) -> None:
        try:
            self._write("quit")
        except BridgeError:
            pass
        try:
            self.proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.proc.kill()

    def close(self) -> None:
        if self.proc.poll() is None:
            try:
                self.proc.stdin.close()
            except OSError:
                pass
            try:
                self.proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.proc.kill()

    # -- convenience ------------------------------------------------------

    @property
    def term_text(self) -> list[str]:
        if not self.frame or not self.frame.get("term"):
            return []
        return [row["g"].rstrip() for row in self.frame["term"]["rows"]]

    def screen(self) -> str:
        return "\n".join(self.term_text)

    def screen_contains(self, needle: str) -> bool:
        return any(needle.lower() in row.lower() for row in self.term_text)

    @property
    def in_dungeon(self) -> bool:
        f = self.frame
        return bool(f and f.get("phase") == "play" and f.get("map"))

    # -- high level -------------------------------------------------------

    def dismiss(self, limit: int = 12) -> None:
        """Clear any pending -more- prompts."""
        for _ in range(limit):
            if not self.screen_contains("-more-"):
                return
            self.key("enter")

    def birth(self, limit: int = 24) -> dict[str, Any]:
        """Roll a random character and enter the game.

        Handles the prompts that appear along the way: the splash screen,
        -more- pauses, and the overwrite confirmation shown when a savefile of
        the same name already exists.
        """
        for _ in range(limit):
            if self.in_dungeon:
                return self.frame
            if self.screen_contains("-more-"):
                self.key("enter")
            elif self.screen_contains("[y/n]") or self.screen_contains("are you sure"):
                self.key("y")
            elif self.screen_contains("press any key") or self.screen_contains("[press"):
                self.key("enter")
            else:
                self.key("@")
        if not self.in_dungeon:
            raise BridgeError(
                "birth did not reach the dungeon; last screen:\n" + self.screen()
            )
        return self.frame

    @property
    def save_dir(self) -> Path | None:
        d = self.hello.get("save_dir") if self.hello else None
        if not d:
            return None
        p = Path(d)
        return p if p.is_absolute() else (self.game_dir / p)

    def wizard_on(self, limit: int = 12) -> bool:
        """Enable wizard (god) mode, answering the confirmation prompts."""
        self.key("C-w")
        for _ in range(limit):
            if self.frame["player"]["wizard"]:
                return True
            if self.screen_contains("-more-"):
                self.key("enter")
            elif self.screen_contains("are you sure"):
                self.key("y")
            else:
                self.key("enter")
        return bool(self.frame["player"]["wizard"])

    def save(self) -> None:
        """Save the game in place (Ctrl-S)."""
        self.key("C-s")
        self.dismiss()

    def debug(self, cmd: str, limit: int = 8) -> dict[str, Any]:
        """Run a wizard debug command (Ctrl-A then a key).

        Angband gates the debug commands behind a one-time confirmation as
        well as the wizard-mode one, so answer whatever it asks.
        """
        self.key("C-a")
        self.key(cmd)
        for _ in range(limit):
            if self.screen_contains("[y/n]") or self.screen_contains("are you sure"):
                self.key("y")
            elif self.screen_contains("-more-"):
                self.key("enter")
            else:
                break
        return self.frame

    def __enter__(self) -> "Bridge":
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()


def main() -> int:
    """Send each argument as a key and print the resulting screen."""
    with Bridge() as b:
        print(f"hello: {b.hello}")
        for spec in sys.argv[1:]:
            b.key(spec)
            print("=" * 78)
            print(f"key {spec!r} -> phase={b.frame.get('phase')} map={'yes' if b.frame.get('map') else 'no'}")
            print(b.screen())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
