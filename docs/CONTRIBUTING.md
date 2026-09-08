# Contributing

## Ground rules

1. **Never break the fork's rebasability.** New behaviour in the engine goes in
   new files. If you think you need to edit an existing Angband source file,
   look for a way to do it from `main-bridge.c` first. See
   [ARCHITECTURE.md](ARCHITECTURE.md).
2. **Never change game rules.** A save produced through the bridge must remain a
   valid Angband save. Balance, content and mechanics belong upstream or in
   `engine/lib/gamedata/`.
3. **Protocol changes are contract changes.** Adding fields is fine. Removing or
   repurposing them needs a version bump and a note in
   [PROTOCOL.md](PROTOCOL.md).

## Setting up

```powershell
./tools/bootstrap.ps1   # MSYS2 + MinGW toolchain (Windows, one time)
./tools/build.ps1
python tools/smoke_test.py
```

On Linux or macOS you need GCC, CMake, Ninja and ncurses, then:

```sh
cd engine
cmake -G Ninja -DSUPPORT_BRIDGE_FRONTEND=ON -DSUPPORT_GCU_FRONTEND=ON -B build
cmake --build build
```

At least one graphical front end must be enabled. With none selected, CMake
picks the Windows front end, which requires libpng and disables the bridge.

## Testing

- `python tools/smoke_test.py` — bridge acceptance tests. Must pass.
- `cmake --build engine/build -t allunittests` — Angband's own 932 unit tests.
  Must stay at 100%.

Both run in CI. A change that breaks upstream tests is a change to game
behaviour, which rule 2 forbids.

## Debugging the bridge by hand

```powershell
python tools/bridge.py enter enter '@' '@' '@' '@'
```

Prints the screen after each keypress. For deeper work, drive it from Python:

```python
import sys; sys.path.insert(0, "tools")
from bridge import Bridge
b = Bridge(savefile="scratch")
b.birth()
b.wizard_on()
b.key(">")
b.debug("m")          # magic map
b.debug("u")          # detect all monsters
print(len(b.frame["monsters"]))
```

Wizard mode and the debug commands make most states reachable in seconds. The
full debug command list is in `engine/src/ui-game.c`, in the `cmd_debug_*`
arrays.

If the client seems to have no effect on the game, you have almost certainly hit
the frame synchronisation contract — see PROTOCOL.md.

## Writing a different client

The protocol is deliberately client-agnostic. A 2D, web or VR client is a
perfectly good contribution. `tools/bridge.py` is the reference implementation
and is small enough to port in an afternoon.

## Style

Engine code follows Angband's existing style: tabs, K&R braces, `/* */`
comments. Client and tool code follows the conventions already in those trees.
Comment *why*, not *what*.
