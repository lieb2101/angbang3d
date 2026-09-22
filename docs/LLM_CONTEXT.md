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
- `docs/LOW_HANGING_FRUIT_PLAN.md`: Full implementation log of 7 graphics/engine features.
- `docs/GENERIC_ENGINE_INTEGRATION.md`: Architectural specification for integrating other roguelikes (NetHack, Moria, DCSS, ADOM).
- `engine-patch/0001-bridge-frontend.patch`: Git patch against Angband 4.2.6.

## Declarative Registries & Extension Points
- **Monster Models (`MonsterModelResolver.cs`)**:
  - `MonsterModelRule`: Record defining model path, scale, ethereal/floating flags, animation speed, equipment role, and optional name matcher function.
  - `MonsterModelResolver.RegisterModelRule(char glyph, ...)`: Add custom humanoid or mesh models with 1 line of code.
  - Equipment roles: `Unarmed`, `Warrior`, `Barbarian`, `Rogue`, `Archer`, `Mage`, `TwoHandedSword`, `TwoHandedAxe`, `TwoHandedStaff`.
- **First-Person Viewmodel (`ViewModel.cs`)**:
  - Articulated low-poly arm rig with dynamically tinted skin and class-specific sleeves/cuffs.
  - Automatic wielded weapon, shield, spellbook, and lit torch matching with walk bobbing and inertia sway.
  - Perspective, reach, and camera FOV scale dynamically with character height stats (`CurrentHeightRatio`).
- **Item Models (`ItemModelResolver.cs`)**:
  - `ModelMappings`: Dictionary mapping item glyphs to 3D models and scaling.
  - `ItemModelResolver.RegisterItemModel(char glyph, string path, float scale)`: Register new item pickups.
  - Procedural materials and meshes are cached to prevent allocation churn.
- **Combat Juice & Visual Feedback (`DungeonWorld.cs`)**:
  - `SpawnFloatingText(string text, Vector3 worldPos, Color color, float scale)`: 3D billboarding floating combat numbers.
  - `SpawnHitSparks(...)`, `SpawnDeathVfx(...)`, `SpawnSpellVfx(...)`: Particle VFX on melee/cast/death.
  - `AddTrauma(float amount)`: Smoothly decaying screen trauma shake on impacts.
  - `ProcessCombatEvents(JsonElement frame)`: Central hook parsing combat messages.
- **Procedural Clutter & Sconces (`DungeonClutterResolver.cs`)**:
  - Corridor torch sconces with point lights placed every 6-8 tiles.
  - Room corner pillars, crates, barrels, and heraldic banners.
- **Audio & Spatial Sound (`AudioManager.cs`)**:
  - Positional 3D sound effects for footsteps, swings, hits, spells, doors, and stairs.
- **Minimap Scaling vs Zoom (`Overlay.cs`)**:
  - Physical window sizing decoupled (`[/]`) from grid zoom radius (`PgUp/PgDn`).

## Known Gotchas, Dead Ends & Lessons Learned
1. **Frame sync off-by-one**: The bridge emits an initial frame when waiting for input before any command is sent. Clients must consume this and check `seq > last_seq`.
2. **`create_needed_dirs()`**: On Windows, `main.c` skipped creating `lib/save`. `main-bridge.c` calls `create_needed_dirs()` during init.
3. **Safety check `bridge_in_play()`**: Frames can be emitted mid-generation. Any coordinate access must check `character_dungeon && character_generated && cave && player && square_in_bounds()`.
4. **Equipment slot lookup assertion**: Calling `slot_by_name()` on optional slots (e.g., weapon, bow, shield) will trigger an engine `assert()` and crash if absent. Use `bridge_get_equipped_by_type(player, EQUIP_*)` which safely scans `player->body.slots`.
5. **Deadlock in Godot stdio**: Never use `OS.ExecuteWithPipe()` — it blocks and deadlocks on shared handles. `BridgeClient.cs` uses `System.Diagnostics.Process` with asynchronous line events.
6. **Patch export encoding**: Never redirect git diff in PowerShell with `>` (writes UTF-16 and breaks `git apply`). Always use `git diff 4.2.6..HEAD --output=..\engine-patch\0001-bridge-frontend.patch`.
7. **Object identification integrity**: `object_kind_name(..., false)` is used so unidentified items show their flavor, not actual identity.
8. **Never use `-n` blindly**: It overwrites existing save files. Client uses unique slot names via `FreeSlot()`.
9. **C# String Interpolation**: Nested quotes inside interpolated strings (`$"...{"NESW"[i]}..."`) cause build failures on Mono SDK; use separate variable lookups.
10. **Map Feature String Encoding**: In the JSON bridge, `map.rows[y].f` encodes tile feature indices as a string of 2 hex characters per tile (`w * 2`). Never index `f[px]` directly as a character; always decode using `parseInt(f.substring(px * 2, px * 2 + 2), 16)`.
11. **Engine Message Ring-Buffer Baseline**: The C engine retains up to 24 previous messages across phase boundaries (e.g. `Accept character history? [y/n]`). When entering active play (`inPlay && !wasInPlay`), the frontend must baseline-seed `prevMessages = frame.messages` and `lastTermRow0` so historical birth prompts are not treated as new in-game messages.

## Token Efficiency & LLM Rate Limit Protocol
1. **Zero-Discovery Startup**: Consult the Key Files Reference above rather than running exploratory searches.
2. **Windowed Reads**: Limit `read_file` to specific line ranges (30-100 lines max).
3. **Targeted Replacements**: Use `replace_string_in_file` / `multi_replace_string_in_file` with 3 lines of context.
4. **Check State**: Fast verify via `python tools/smoke_test.py` (0.5s) and `dotnet build client/angband3d.csproj` (1.3s).
5. **Session Continuity**: Record any pending work or newly discovered gotchas in `docs/NEXT_STEPS.md` before concluding.
