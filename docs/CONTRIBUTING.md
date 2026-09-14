# Contributing to angband3d

Welcome! `angband3d` is a community-driven project bringing the rich, deep gameplay of Angband into a first-person 3D dungeon crawler using Godot 4 and C#.

Whether you are interested in adding 3D models, composing ambient music and sound effects, designing PBR dungeon materials, polishing the UI/HUD, improving performance, or writing alternate frontends (Web, VR, mobile), your contributions are welcome!

---

## Table of Contents
1. [Core Principles & Ground Rules](#core-principles--ground-rules)
2. [Development Environment Setup](#development-environment-setup)
3. [Architecture & Project Tour](#architecture--project-tour)
4. [Contribution Recipes & How-Tos](#contribution-recipes--how-tos)
   - [Recipe 1: Adding a 3D Monster Model & Rig](#recipe-1-adding-a-3d-monster-model--rig)
   - [Recipe 2: Adding a 3D Item Pickup](#recipe-2-adding-a-3d-item-pickup)
   - [Recipe 3: Adding Combat Juice, Audio & VFX](#recipe-3-adding-combat-juice-audio--vfx)
   - [Recipe 4: Customizing Shaders, Materials & Geometry](#recipe-4-customizing-shaders-materials--geometry)
   - [Recipe 5: Adding First-Person Weapon Rigs & Animations](#recipe-5-adding-first-person-weapon-rigs--animations)
   - [Recipe 6: Tuning Procedural 3D Audio Synthesis](#recipe-6-tuning-procedural-3d-audio-synthesis)
   - [Recipe 7: Modifying the Engine Bridge](#recipe-7-modifying-the-engine-bridge)
5. [Input & Coordinate Conventions](#input--coordinate-conventions)
6. [Testing & Verification](#testing--verification)
7. [Packaging Standalone Releases](#packaging-standalone-releases)
8. [Submitting a Pull Request](#submitting-a-pull-request)
9. [Code Style & Conventions](#code-style--conventions)

---

## Core Principles & Ground Rules

1. **Preserve the engine fork's rebasability**:
   - All bridge logic in `engine/` lives in new, isolated files (`src/main-bridge.c`, `src/bridge-json.c`, `src/bridge-json.h`, `BRIDGE_Frontend.cmake`).
   - Only 4 upstream files are modified to register the frontend (`main.c`, `main.h`, `CMakeLists.txt`, `src/Makefile.src`).
   - Game logic, formulas, content, and RNG are never modified in the C engine.
2. **Standard Angband Save Compatibility**:
   - Savefiles produced or loaded through the 3D frontend must remain 100% binary-compatible with upstream Angband 4.2.6.
3. **Protocol Stability**:
   - The JSON IPC protocol is a versioned contract. Fields can be added freely; removing or renaming fields requires a protocol version increment in [PROTOCOL.md](PROTOCOL.md).
4. **Permissive Asset Licensing**:
   - All 3D models, textures, audio, and fonts committed to the repository must be CC0, Public Domain, MIT, or compatible open-source licences, with clear attribution in `client/assets/CREDITS.md`.

---

## Development Environment Setup

### Prerequisites
- **Godot Engine (.NET / Mono version)**: Godot 4.3 or 4.4 (.NET edition).
- **.NET SDK**: .NET 8.0 or 9.0 SDK.
- **Python**: Python 3.8+ (for automated smoke tests and reference bridge scripts).
- **C/C++ Build Toolchain**:
  - *Windows*: MSYS2 with MinGW-w64 (automated via `tools/bootstrap.ps1`).
  - *Linux / macOS*: Standard GCC or Clang, CMake, Ninja, and ncurses packages.

### Windows Quickstart

```powershell
# 1. Run one-time toolchain bootstrapping (installs MSYS2 + MinGW packages if needed)
.\tools\bootstrap.ps1

# 2. Build the Angband C engine
.\build.cmd

# 3. Build the Godot C# client
dotnet build client/angband3d.csproj

# 4. Run automated acceptance tests
python tools/smoke_test.py

# 5. Play the game!
.\play.cmd
```

### Linux & macOS Quickstart

```sh
# 1. Build the Angband engine with the bridge frontend enabled
cd engine
cmake -G Ninja -DSUPPORT_BRIDGE_FRONTEND=ON -DSUPPORT_GCU_FRONTEND=ON -B build
cmake --build build

# 2. Build the C# client
cd ../client
dotnet build angband3d.csproj

# 3. Run bridge tests
cd ..
python3 tools/smoke_test.py
```

### Running from the Godot Editor
1. Open the Godot 4 .NET editor.
2. Choose **Import** and select `client/project.godot`.
3. Ensure the engine is compiled (`engine/build/game/angband.exe` or `angband` binary exists).
4. Press **F5** in Godot to run the main scene (`Main.tscn`).

---

## Architecture & Project Tour

```
angband3d/
├── client/                     # Godot 4 (.NET / C#) 3D frontend
│   ├── assets/                 # CC0 3D models, textures, fonts
│   │   ├── CREDITS.md          # Asset attributions & licenses
│   │   └── models/             # Characters, dungeon props, items
│   ├── scripts/                # C# Game logic & rendering controllers
│   │   ├── BridgeClient.cs     # Child process IPC & async JSON stream parser
│   │   ├── DungeonWorld.cs     # 3D world geometry, MultiMesh, lighting & camera
│   │   ├── MonsterModelResolver.cs # 3D character models, shaders & procedural tokens
│   │   ├── ItemModelResolver.cs    # 3D pickup models, rotation & hover effects
│   │   ├── Main.cs             # Lifecycle coordinator, input dispatcher & menu router
│   │   ├── Overlay.cs          # 2D HUD, mini-map, 80x24 terminal & menu canvas
│   │   └── AngbandColors.cs    # 16-color palette mapping from Angband curses
│   ├── Main.tscn               # Root scene
│   └── project.godot           # Godot project settings
├── engine/                     # Angband 4.2.6 source tree (C)
│   ├── src/main-bridge.c       # JSON bridge frontend implementation
│   ├── src/bridge-json.c       # Lightweight JSON serializer
│   └── src/bridge-json.h       # JSON builder declarations
├── engine-patch/               # Rebasable patch against Angband 4.2.6
├── tools/                      # Automation, smoke tests & reference scripts
│   ├── bootstrap.ps1           # MSYS2 / MinGW environment setup
│   ├── build.ps1               # Engine build pipeline
│   ├── bridge.py               # Reference Python driver for protocol v1
│   ├── smoke_test.py           # 11-test acceptance suite
│   └── fetch_assets.ps1        # CC0 asset download helper
└── docs/                       # Architecture, Protocol & Handover documentation
```

---

## Contribution Recipes & How-Tos

### Recipe 1: Adding a 3D Monster Model

Monster models are mapped declaratively in `client/scripts/MonsterModelResolver.cs`. You can map any `.glb` or `.gltf` asset in `client/assets/models/characters/` to an Angband monster glyph or specific race name.

#### Basic glyph mapping:
```csharp
// Inside MonsterModelResolver.InitModelRules():
RegisterModelRule('k', "res://assets/models/characters/Kobold.glb", scale: 0.75f, speed: 1.1f);
```

#### Name-filtered mapping (specifying bosses or unique variants):
```csharp
RegisterModelRule('s', "res://assets/models/characters/Skeleton_King.glb", scale: 1.3f,
    matcher: name => name.Contains("king") || name.Contains("lord"));
```

#### Ethereal / Translucent monsters (Ghosts, Wraiths):
```csharp
RegisterModelRule('G', "res://assets/models/characters/Ghost.glb", scale: 1.0f,
    isEthereal: true, isFloating: true);
```

#### How animations work:
If your GLTF/GLB contains an `AnimationPlayer`, `MonsterModelResolver` automatically binds animations named `Walk`, `Running_A`, or `Run` for locomotion, and `Idle`, `Combat_Idle`, or `Static` for idle states.

---

### Recipe 2: Adding a 3D Item Pickup

Item pickups are registered in `client/scripts/ItemModelResolver.cs` using the `ModelMappings` dictionary or via `RegisterItemModel`:

```csharp
// Register an item glyph with a 3D model path and scale factor:
ItemModelResolver.RegisterItemModel('!', "res://assets/models/dungeon/bottle_A_green.gltf.glb", scale: 0.60f);
ItemModelResolver.RegisterItemModel('?', "res://assets/models/characters/spellbook_closed.gltf", scale: 0.60f);
```

If an item glyph does not have a 3D model assigned, `ItemModelResolver` automatically creates a procedural 3D rune token with emissive colors matching Angband's color attribute (`attr`).

---

### Recipe 3: Adding Combat Juice, Audio & VFX

`DungeonWorld.cs` provides decoupled hooks for combat feedback and visual effects:

- **Floating Damage / Combat Text**:
  ```csharp
  _world.SpawnFloatingText("-14 HP", monsterWorldPos, Colors.Red, scale: 1.2f);
  ```
- **Kinetic Screen Trauma / Shake**:
  ```csharp
  _world.AddTrauma(0.35f); // Smoothly decays over time
  ```
- **Parsing Combat Messages**:
  In `DungeonWorld.ProcessCombatEvents(JsonElement frame)`, Angband's `messages` array is monitored. You can trigger sound effects, particle emitters, or camera pulses based on combat keywords (e.g., `"hits you"`, `"misses"`, `"dies"`).

---

### Recipe 4: Customizing Shaders, Materials & Geometry

Dungeon walls, floors, doors, and features are constructed in `client/scripts/DungeonWorld.cs`:
- **MultiMesh Batches**: Repeated static elements (walls, floors, ceiling tiles, stairs, rubble) are batched using `MultiMeshInstance3D` to keep draw calls minimal ($\le 10$ draw calls for an entire 12,000-tile dungeon level).
- **Procedural PBR Textures**: High-resolution procedural masonry textures, normal maps, and emissive veins (Magma, Quartz, Lava) are generated in `CreateTexture()` and cached in static materials.
- **Orientation-Aware Doors**: Corridors dynamically choose door orientation (North-South vs East-West) by inspecting adjacent orthogonal walkable cells in `BuildDoorMesh()`.
- **Depth Biomes**: Biome parameters (fog density, ambient lighting energy, particle emitters) are configured via `GetBiomeForDepth(int depth)`.

---

### Recipe 5: Adding First-Person Weapon Rigs & Animations

Viewmodels and player arm kinematics are managed in `client/scripts/ViewModel.cs`:
- **Held Equipment Matching**: `ViewModel.cs` inspects `frame.player.weapon_item`, `bow_item`, and `shield_item` every frame.
- **Procedural Kinematics**:
  - `UpdateWalkBobbing(double delta, bool moving)` generates natural sinusoidal vertical and lateral arm movements.
  - `UpdateWeaponSway(double delta)` creates smooth camera inertia lag when turning.
  - `TriggerMeleeAttack()`, `TriggerRangedAttack()`, and `TriggerCastSpell()` initiate kinetic slash, thrust, release, and glowing surge animations.
- **Racial Proportions**: Arm length, hand scale, and resting position dynamically adapt to `frame.player.ht` and player race.

---

### Recipe 6: Tuning Procedural 3D Audio Synthesis

`client/scripts/AudioManager.cs` generates all game audio dynamically as 16-bit PCM waveform buffers:
- **Procedural Sounds**: Footsteps (dungeon stone vs outdoor gravel), weapon slashes/impacts, spell zaps, wooden door creaks, funeral death bells, and staircase transitions.
- **Pitch Modulation**: Footstep frequency automatically shifts lower for heavy races (Half-Troll, Dwarf) and higher for light races (Halfling, Gnome).
- **Zero Allocations**: Sounds are synthesized on initialization and cached in pre-allocated `AudioStreamPlayer3D` and `AudioStreamPlayer` sound pools.

---

### Recipe 7: Modifying the Engine Bridge

If you need to expose additional Angband state to the JSON stream:
1. Open `engine/src/main-bridge.c` or `engine/src/bridge-json.c`.
2. Add your structured field to `bridge_emit_frame()`.
3. Update [PROTOCOL.md](PROTOCOL.md) to document the new field.
4. Test with `python tools/smoke_test.py`.
5. Update the git patch file:
   ```powershell
   cd engine
   git diff 4.2.6..HEAD --output=..\engine-patch\0001-bridge-frontend.patch
   ```
   *(Note: Always use `--output=`, never `>` to avoid UTF-16 encoding errors on Windows PowerShell).*

---

## Input & Coordinate Conventions

### Camera & Directional Movement
- **Camera Facing**:
  - `0` = North ($-Z$)
  - `1` = East ($+X$)
  - `2` = South ($+Z$)
  - `3` = West ($-X$)
- **Arrow Keys vs Number Pad**:
  - `Left` / `Right` arrows rotate camera facing 90° (0-turn camera action).
  - `Up` / `Down` arrows step forward / backward relative to camera facing.
  - Number pad `4` / `6` **strafe** left / right relative to camera facing.
  - Number pad `8` / `2` step forward / backward relative to camera facing.
  - Number pad `7` / `9` / `1` / `3` take diagonal steps relative to camera facing.
  - Number pad `5` stays / rests in place.

### Coordinate Systems
- **Angband Engine**: Grid `(x, y)` where `x` is horizontal column and `y` is vertical row.
- **Godot 3D World**:
  - World $X = 2 \times x$
  - World $Z = 2 \times y$
  - World $Y = 0$ (floor) to $Y = 3.0\text{ m}$ (ceiling)

---

## Testing & Verification

Before submitting changes, run the automated verification suite:

```powershell
# 1. Godot C# compilation (0 warnings, 0 errors)
dotnet build client/angband3d.csproj

# 2. Bridge protocol & game acceptance tests
python tools/smoke_test.py

# 3. Upstream Angband unit tests (in MSYS2 / Linux)
cd engine; cmake --build build -t allunittests
```

### Driving the Bridge via Python
You can script the game directly from Python for rapid iteration or testing:

```python
import sys; sys.path.insert(0, "tools")
from bridge import Bridge

b = Bridge(savefile="scratch")
b.birth()
b.wizard_on()
b.key(">")            # descend stairs
b.debug("m")          # magic map full level
b.debug("u")          # detect all monsters
print(f"Monsters visible: {len(b.frame['monsters'])}")
```

---

## Packaging Standalone Releases

To test the standalone release packaging pipeline locally:

```powershell
# Run the automated packaging script:
.\tools\package.ps1
```

This will:
1. Compile the native Angband C engine.
2. Export the headless Godot standalone executable `Angband3D.exe` and `Angband3D.pck`.
3. Assemble the distribution structure under `dist/Angband3D-Windows-x64/`.
4. Bundle and compress the output into `dist/Angband3D-Windows-x64.zip`.

Verify that extracting the zip and double-clicking `Angband3D.exe` starts the game cleanly without any developer dependencies installed.

---

## Submitting a Pull Request

1. **Fork the repository** on GitHub.
2. **Create a feature branch** (`git checkout -b feature/awesome-feature`).
3. **Commit your changes** with descriptive commit messages.
4. **Run the contributor pre-flight checklist**:
   - [ ] C# code builds with zero errors or warnings (`dotnet build client/angband3d.csproj`).
   - [ ] All 11 smoke tests pass (`python tools/smoke_test.py`).
   - [ ] Upstream savefiles remain 100% binary compatible.
   - [ ] Any modified engine C files have their patch updated (`engine-patch/0001-bridge-frontend.patch`).
   - [ ] Any newly added assets are CC0/MIT with license attribution in `client/assets/CREDITS.md`.
5. **Open a Pull Request** against the `main` branch with a summary of changes, rationale, and screenshots/GIFs for visual features.

---

## Code Style & Conventions

- **C# / Godot Code**:
  - Follow standard .NET conventions: `PascalCase` for classes and public methods, `_camelCase` for private fields.
  - Prefer immutable records / properties for data payloads.
  - Avoid per-frame allocations (`GC churn`) inside `_Process` and `UpdateDungeon`.
- **C Engine Code**:
  - Match Angband's style: tabs for indentation, K&R brace style, C89/C99 compatibility.
  - Contain all bridge logic within `src/main-bridge.c` and `src/bridge-json.c`.
- **Documentation**:
  - Document *why* rather than *what*. Provide XML doc comments on public APIs and helper methods.


