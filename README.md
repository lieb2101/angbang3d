# angband3d

A first-person 3D front end for [Angband](https://github.com/angband/angband),
built as a fork of the game rather than a reimplementation of it.

The goal is *Dungeon Master*-style presentation with Angband's full roguelike
depth: every monster, item, artifact, curse and level generator, unchanged.

**Status: Phase 1 — the bridge works.** There is no 3D renderer yet.

## How it works

Angband's game logic is well separated from its display layer, and adding a new
front end is a supported extension point. So instead of rewriting the game we
add one:

```
  engine/  (forked Angband)            client/  (Godot 4)
  +-------------------------+          +--------------------+
  |  Angband, unmodified    |  stdout  |                    |
  |                         | -------> |  reads JSON frames |
  |  src/main-bridge.c      |          |  renders the world |
  |    a Term that emits    | <------- |  sends keypresses  |
  |    JSON instead of text |  stdin   |                    |
  +-------------------------+          +--------------------+
```

The bridge publishes two channels every time the game waits for input:

- **Structured** — player state, terrain, visible monsters and objects,
  messages, light radius. This drives the 3D world.
- **Raw terminal** — the 80x24 character grid. Angband's prompts, menus and
  stores are entangled with its game logic, so they are shown as a text overlay
  and replaced with native UI panel by panel.

Input is delivered as *keypresses*, not as game commands. That means character
creation, inventory, stores, targeting, saving and wizard mode all work through
the bridge with no special casing.

## Design rules

1. **The engine fork stays rebasable.** Bridge code lives in new files. Only
   four existing files are touched, and only to register the front end. We can
   pull upstream Angband releases indefinitely.
2. **The game is never modified.** Saves written through the bridge are
   ordinary Angband saves.
3. **The protocol is versioned** so alternate clients stay possible.

## Building

Requires MSYS2 with the MinGW-w64 toolchain on Windows, or GCC/CMake elsewhere.

```powershell
# one-time toolchain setup
./tools/bootstrap.ps1

# build
./tools/build.ps1

# verify
python tools/smoke_test.py
```

At least one graphical front end must be enabled or CMake selects the Windows
front end, which disables the bridge. The build scripts enable GCU, which also
gives you a playable text Angband in the same binary:

```
cd engine/build/game && ./angband.exe -mgcu
```

## Running the client

Needs [Godot 4 (.NET build)](https://godotengine.org/download) and the .NET 8
SDK.

```powershell
godot --path client
```

It launches the engine, rolls a random character and drops you into the town.
Movement keys play the game; **Tab** switches between the map view and the raw
terminal view, which is where prompts, menus and stores appear.

Squares you can currently see are drawn lit; squares you remember but cannot see
are dimmed. That distinction comes straight from Angband and is what becomes
fog-of-memory rendering in 3D.

## Trying the bridge by hand

```powershell
python tools/bridge.py enter enter '@' '@' '@' '@'
```

Rolls a random character and prints the screen after each keypress.

## Licence

GPL v2. angband3d is a derivative work of Angband, which is dual licensed under
the GPL v2 and the traditional Angband licence; this project takes the GPL v2
option. See [LICENSE](LICENSE) and `engine/docs/copying.rst`.

Angband's content draws heavily on Tolkien. That is fine for a free,
non-commercial project, but see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
before distributing this in any form that accepts money.
