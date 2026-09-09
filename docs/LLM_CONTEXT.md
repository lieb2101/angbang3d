# angband3d — AI Context & Onboarding Guide

## Mission & Architecture
`angband3d` is a first-person 3D dungeon crawler frontend for Angband 4.2.6 (C engine + Godot 4.7.2 Mono/C# client).
Rather than rewriting Angband rules in C#, Angband runs as a headless child process communicating via line-delimited JSON over stdio.

```
+------------------------------------+             +-------------------------------------+
| engine/ (Angband 4.2.6 C)          |  JSON stdout | client/ (Godot 4.7.2 C# client)      |
|  - main-bridge.c (JSON Term/events)| -----------> |  - BridgeClient.cs (Process stdio)  |
|  - bridge-json.c                   |              |  - DungeonWorld.cs (3D mesh/camera) |
|  - Minimal changes to 4 core files | <----------- |  - Overlay.cs (2D Map/Term/HUD/Menu)|
+------------------------------------+  stdin keys  |  - Main.cs (view/input coordinator) |
                                                    +-------------------------------------+
```

## Critical Invariants & Rules
1. **Never break Angband rebasability**:
   - Upstream engine modifications MUST stay isolated to `main-bridge.c`, `bridge-json.c`, `bridge-json.h`, and `BRIDGE_Frontend.cmake`.
   - Only 4 core files are touched to register the module: `main.c`, `main.h`, `CMakeLists.txt`, `src/Makefile.src`.
   - When modifying engine code, always refresh the patch:
     ```powershell
     cd engine; git diff 4.2.6..HEAD --output=..\engine-patch\0001-bridge-frontend.patch
     ```
2. **Never change game rules or save format**:
   - Saves are standard Angband saves stored in `engine/build/game/lib/save/`.
   - Game logic and RNG are 100% authoritative in Angband C.
3. **Turn/Input Architecture**:
   - Input is delivered as keypresses (`key <spec>` or `keys <text>`), NOT `cmdq_push()`, preserving menus, prompts, wizard mode, and birth screens.
   - Turning (Left/Right arrow) is camera-only, instant, 0-turn, and NOT gated on engine busy state.
   - Moving (Up/Down arrow) sends `up`/`down` relative to current camera facing (`N`, `E`, `S`, `W`).
   - `Shift-M` = 2D Map toggle. `m` = Cast spell / magic selection in Angband.
   - `Escape` in dungeon opens in-game pause menu (Resume, Quick Save `Ctrl-S`, Load, Save & Quit `Ctrl-X`, Exit).
   - `Ctrl-W` = Wizard mode toggle. `Ctrl-A` = Wizard debug commands.
4. **View Routing Rule (`NeedsTerminal`)**:
   - `phase != "play"` -> Terminal overlay (splash, birth, level gen).
   - `ui.overlay > 0` -> Terminal overlay (inventory, stores, character sheet).
   - `!awaiting_command && !more` -> Terminal overlay (direction/target/item/yes-no prompts).
   - `more == true` (-more- message pause) -> STAYS IN 3D WORLD, drawn highlighted on HUD.
5. **Terrain vs Glyph**:
   - 3D world geometry MUST be built from map `f` (feature index), NOT `g` (glyph), because glyphs get overwritten by monsters standing on tiles.
   - Engine sends a one-time `features` message containing all feature names, glyphs, and passability flags (`f_info`).

## Quick Build & Run Commands
- **Launch Game**: `.\play.cmd` (or `.\play.ps1 -Character <name> [-Random|-Manual]`)
- **Build Engine**: `.\build.cmd` (runs CMake Ninja inside MSYS2 MinGW64)
- **Build Client**: `dotnet build client/angband3d.csproj`
- **Run Acceptance Tests**: `python tools/smoke_test.py` (11 tests, zero dependencies)
- **Run Upstream Tests**: Run in MSYS2: `cd engine && cmake --build build -t allunittests` (932 tests)
- **Automated Client Test**: `godot --path client -- --save=test --keys=... --screenshot=out.png --shot-after=180`

## Key Files Reference
- `engine/src/main-bridge.c`: The C bridge emitting JSON frames and receiving key commands.
- `client/scripts/BridgeClient.cs`: Handles child process stdio via `System.Diagnostics.Process` (avoid Godot's `OS.ExecuteWithPipe`).
- `client/scripts/Main.cs`: Main loop, input routing, start/pause menus, script test runner.
- `client/scripts/DungeonWorld.cs`: 3D procedural grid mesh, camera tweening, torch lighting, entity billboards.
- `client/scripts/Overlay.cs`: 2D canvas drawing HUD, classic 2D map overlay, 80x24 terminal overlay, and main/pause menus.
- `docs/PROTOCOL.md`: JSON wire specification.
- `docs/ARCHITECTURE.md`: Technical trade-offs and rationale.
- `engine-patch/0001-bridge-frontend.patch`: Git patch against Angband 4.2.6.

## Known Gotchas & Lessons Learned
1. **Frame sync off-by-one**: The bridge emits an initial frame when waiting for input before any command is sent. Clients must consume this and check `seq > last_seq`.
2. **`create_needed_dirs()`**: On Windows, `main.c` skipped creating `lib/save`. `main-bridge.c` calls `create_needed_dirs()` during init.
3. **Safety check `bridge_in_play()`**: Frames can be emitted mid-generation. Any coordinate access must check `character_dungeon && character_generated && cave && player && square_in_bounds()`.
4. **Object identification integrity**: `object_kind_name(..., false)` is used so unidentified items show their flavor, not actual identity.
5. **Never use `-n` blindly**: It overwrites existing save files. Client uses unique slot names via `FreeSlot()`.
