# angband3d

A first-person 3D front end for [Angband](https://github.com/angband/angband),
built as a fork of the game rather than a reimplementation of it.

The goal is *Dungeon Master*-style presentation with Angband's full roguelike
depth: every monster, item, artifact, curse and level generator, unchanged.

**Status: Phase 2b — fully playable 3D dungeon crawler with Godot 4.**

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

## Playing

Double-click **`play.cmd`**, or from a terminal:

```
C:\Dev\angband3d\play.cmd
```

It launches the engine, rolls a random character and drops you into the town.

| key | action |
|---|---|
| Left / Right | turn camera 90° (instant, 0 turns) |
| Up / Down | step forward / backward |
| `hjkl` | cardinal grid move |
| `>` / `<` | descend / ascend stairs |
| `Shift-M` | toggle 2D classic map overlay |
| `PgUp` / `PgDn` | scale HUD minimap |
| `Tab` | toggle raw terminal view |
| `Escape` | pause menu (save, load, quit) |
| `i` | inventory |
| `Ctrl-S` | quick save |
| `Ctrl-X` | save and quit |
| `Ctrl-W` | wizard (god) mode |
| `Ctrl-A` | wizard debug menu |
| `?` | help |

Prompts, menus and stores appear in the **terminal view** (`Tab`) — those parts
of Angband are inseparable from its game logic, so they are shown as text until
native panels replace them.

Squares you can currently see are drawn lit; squares you remember but cannot see
are dimmed. That distinction comes straight from Angband and is what becomes
fog-of-memory rendering in 3D.

To play plain text Angband instead:

```
play.cmd -Classic
```

The `.cmd` wrappers exist because PowerShell refuses to run unsigned scripts by
default; they bypass that for the one script rather than changing any
system-wide policy. If you would rather run the `.ps1` files directly, use
`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.

## Building

Requires MSYS2 with the MinGW-w64 toolchain on Windows, or GCC/CMake elsewhere.

```
tools\bootstrap.ps1   # one-time toolchain setup
build.cmd             # build
python tools\smoke_test.py
```

At least one graphical front end must be enabled or CMake selects the Windows
front end, which disables the bridge. The build scripts enable GCU, which also
gives you a playable text Angband in the same binary.

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
