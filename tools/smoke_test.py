"""Phase 1 acceptance tests for the Angband JSON bridge.

Self-contained: no pytest, no third-party packages, so CI stays trivial.

    python tools/smoke_test.py
"""

from __future__ import annotations

import sys
import traceback
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from bridge import Bridge, BridgeError  # noqa: E402

TESTS: list = []
SAVE_PREFIX = "smoke"


def test(fn):
    TESTS.append(fn)
    return fn


def check(cond: bool, what: str) -> None:
    if not cond:
        raise AssertionError(what)


# ---------------------------------------------------------------------------


@test
def test_hello_handshake():
    """The bridge announces itself with a protocol version and save paths."""
    with Bridge(savefile=f"{SAVE_PREFIX}_hello") as b:
        h = b.hello
        check(h["t"] == "hello", "first message must be hello")
        check(h["protocol"] == 1, f"unexpected protocol {h.get('protocol')}")
        check("Angband" in h["build"], f"unexpected build {h.get('build')}")
        check(bool(h["save_dir"]), "save_dir must be reported")


@test
def test_initial_frame_precedes_commands():
    """A frame is available before any command is sent (client sync contract)."""
    with Bridge(savefile=f"{SAVE_PREFIX}_sync") as b:
        check(b.frame is not None, "expected an initial frame at connect")
        check(b.seq == 1, f"initial frame should be seq 1, got {b.seq}")
        before = b.seq
        b.key("enter")
        check(b.seq > before, "seq must advance after a key")


@test
def test_birth_reaches_dungeon():
    """A random character can be created and lands in the town."""
    with Bridge(savefile=f"{SAVE_PREFIX}_birth") as b:
        b.birth()
        p = b.frame["player"]
        check(b.frame["phase"] == "play", "phase should be play")
        check(bool(p["name"]), "player should have a name")
        check(bool(p["race"]) and bool(p["class"]), "race/class should be set")
        check(p["hp"] > 0 and p["hp_max"] > 0, "player should have hit points")
        check(p["level"] >= 1, "player should be at least level 1")
        check(p["depth"] == 0, "should start in town")


@test
def test_map_encoding_is_consistent():
    """Every map row's parallel encodings have the documented widths."""
    with Bridge(savefile=f"{SAVE_PREFIX}_map") as b:
        b.birth()
        m = b.frame["map"]
        check(m is not None, "map must be present in play phase")
        w, h = m["w"], m["h"]
        check(w > 0 and h > 0, "map must have positive dimensions")
        check(len(m["rows"]) == h, "row count must equal height")
        for i, row in enumerate(m["rows"]):
            check(len(row["g"]) == w, f"row {i}: glyph width {len(row['g'])} != {w}")
            check(len(row["a"]) == w * 2, f"row {i}: attr width != 2*{w}")
            check(len(row["f"]) == w * 2, f"row {i}: feat width != 2*{w}")
            check(len(row["l"]) == w, f"row {i}: flag width != {w}")
            int(row["a"], 16)
            int(row["f"], 16)
            int(row["l"], 16)


@test
def test_player_glyph_present_on_map():
    """The player appears on the structured map at the reported coordinates."""
    with Bridge(savefile=f"{SAVE_PREFIX}_glyph") as b:
        b.birth()
        p, m = b.frame["player"], b.frame["map"]
        check(0 <= p["y"] < m["h"] and 0 <= p["x"] < m["w"], "player out of map bounds")
        check(m["rows"][p["y"]]["g"][p["x"]] == "@", "player glyph not at player position")


@test
def test_term_channel():
    """The raw 80x24 terminal channel is delivered intact."""
    with Bridge(savefile=f"{SAVE_PREFIX}_term") as b:
        b.birth()
        t = b.frame["term"]
        check(t["w"] == 80 and t["h"] == 24, f"expected 80x24, got {t['w']}x{t['h']}")
        check(len(t["rows"]) == 24, "expected 24 term rows")
        for row in t["rows"]:
            check(len(row["g"]) == t["w"], "term glyph row width mismatch")
            check(len(row["a"]) == t["w"] * 2, "term attr row width mismatch")


@test
def test_wizard_mode_and_detection():
    """God mode enables, and detection populates the monster channel."""
    with Bridge(savefile=f"{SAVE_PREFIX}_wiz") as b:
        b.birth()
        check(b.frame["player"]["noscore"] == 0, "should start un-cheated")
        check(b.wizard_on(), "wizard mode should enable")
        check(b.frame["player"]["noscore"] != 0, "noscore flag should be set")

        b.key(">")
        b.dismiss()
        check(b.frame["player"]["depth"] == 1, "should be on dungeon level 1")
        check(b.frame["player"]["light"] > 0, "should have a lit torch in the dungeon")

        b.debug("m")
        b.debug("u")
        mons = b.frame["monsters"]
        check(len(mons) > 0, "detect-all-monsters should reveal monsters")
        for mon in mons:
            check("race" in mon, "detected monster missing race")
            check(mon["hp_max"] > 0, "monster should have hit points")
            check(len(mon["glyph"]) == 1, "monster glyph should be one char")

        known = sum(1 for r in b.frame["map"]["rows"] for c in r["l"] if int(c, 16) & 1)
        check(known > 100, f"magic map should reveal terrain, got {known} tiles")


@test
def test_save_load_roundtrip():
    """A saved character reloads identically, with cheat flags preserved."""
    name = f"{SAVE_PREFIX}_rt"
    with Bridge(savefile=name) as b:
        b.birth()
        b.wizard_on()
        before = dict(b.frame["player"])
        b.key("C-s")
        check(b.screen_contains("done"), f"save failed: {b.term_text[0]!r}")
        b.quit()

    with Bridge(savefile=name, new_character=False) as c:
        c.dismiss()
        for _ in range(8):
            if c.in_dungeon:
                break
            c.key("enter")
        after = c.frame["player"]
        check(after["name"] == before["name"], "name changed across save/load")
        check(after["race"] == before["race"], "race changed across save/load")
        check(after["class"] == before["class"], "class changed across save/load")
        check(after["hp_max"] == before["hp_max"], "max hp changed across save/load")
        # The wizard toggle resets on load but the cheat flag is permanent.
        check(after["noscore"] == before["noscore"], "noscore flag not persisted")


@test
def test_messages_channel():
    """Game messages are exposed as structured entries."""
    with Bridge(savefile=f"{SAVE_PREFIX}_msg") as b:
        b.birth()
        b.wizard_on()
        msgs = b.frame["messages"]
        check(isinstance(msgs, list), "messages must be a list")
        check(any("wizard" in m["text"].lower() for m in msgs),
              "expected a wizard-mode message")


# ---------------------------------------------------------------------------


def main() -> int:
    passed = failed = 0
    for fn in TESTS:
        label = fn.__name__.replace("test_", "").replace("_", " ")
        try:
            fn()
        except (AssertionError, BridgeError) as exc:
            failed += 1
            print(f"FAIL  {label}\n        {exc}")
        except Exception:  # noqa: BLE001
            failed += 1
            print(f"ERROR {label}")
            traceback.print_exc()
        else:
            passed += 1
            print(f"ok    {label}")

    print(f"\n{passed} passed, {failed} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
