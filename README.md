# angband3d

A first-person 3D front end for [Angband](https://github.com/angband/angband),
built as a high-performance fork of the game rather than a reimplementation of it.

The goal is *Dungeon Master*-style immersion with Angband's full roguelike depth: every monster, item, artifact, curse, vault, and level generator, completely unchanged.

**Status: Version 1.0.0 — fully playable standalone 3D dungeon crawler with Godot 4 & .NET 8.**

[![License: GPL v2](https://img.shields.io/badge/License-GPL_v2-blue.svg)](LICENSE)
[![Engine: Angband 4.2.6](https://img.shields.io/badge/Angband-4.2.6-darkgreen.svg)](https://github.com/angband/angband)
[![Client: Godot 4.3+ .NET](https://img.shields.io/badge/Godot-4.3+_.NET-blueviolet.svg)](https://godotengine.org/)
[![Smoke Tests: 11/11 Passing](https://img.shields.io/badge/Smoke_Tests-11%2F11_Passing-success.svg)](tools/smoke_test.py)

---

## 🌟 Notable Features Up Front — Why 3D is Worth Playing

### 📏 1. Dynamic Character Height & World Scaling (The Scaling Feature)
Your character's rolled race and physical height stat directly scale the entire 3D world:
- **Halflings & Gnomes (~36–44")**: Lower camera eye height (~0.88m–1.05m) with a wider perspective. You look upward at looming doors, stone archways, and towering trolls, giving a palpable sense of subterranean dread.
- **Dwarves (~48–56")**: Grounded eye height (~1.15m–1.30m) with rapid, sturdy footing.
- **Humans & Elves (~68–76")**: Standard eye height (~1.62m–1.76m) with balanced dungeon perspective.
- **Half-Trolls & High-Elves (~84–104")**: Commanding eye height (~1.95m–2.35m). You peer down corridors from near the ceiling rafters with heavy, echoing footsteps.
- **Dynamic Viewmodel Proportions**: Hand sizes, arm lengths, reach distances, and weapon scale adapt proportionally to your race.
- **Perspective-Correct FOV & Audio**: Field of view smoothly scales (86° for short races, 82° for giant races), and footstep acoustic pitch modulates with character mass.

### ⚔️ 2. Animated First-Person Viewmodel & Dynamic Hands
- **Articulated Player Hands**: Modeled 3D arms featuring tapered sleeves, metallic wrist bracers, palms, and sculpted fingers wrapping around weapon handles.
- **Dynamic Racial Skin & Sleeves**: Automatically skins your hands and sleeves with race-specific skin tones and class-tailored cloth colors.
- **Equipped Weapon & Item Matching**: Real-time 3D models for swords, daggers, 2H battleaxes, polearms, bows, crossbows, wands, staves, spellbooks, shields, burning torches, or martial bare fists.
- **Fluid First-Person Kinematics**: Natural sinusoidal walk bobbing, inertia yaw/pitch sway, melee slash and thrust animations, glowing spellcast surges, and hit trauma recoil.

### 🐉 3. Living 3D Bestiary & Procedural Anatomical Creatures
- **Role-Accurate Humanoid Equipment**: Guards wield swords and shields; archers draw crossbows; mages hold glowing staves; thieves wield daggers; and beggars fight unarmed.
- **Anatomical Procedural 3D Creature Tokens**: Hundreds of non-humanoid monsters (dragons, hydras, giant spiders, centipedes, beholders, slimes, basilisks, demons, elementals, kobolds, imps, yeeks, yetis, lice) feature dedicated procedural anatomical 3D models with undulating segments, skittering legs, flapping wings, glowing irises, and pulsating nuclei.
- **Robust Skeletal Rigging & T-Pose Prevention**: Bestiary models without embedded skeletal animation tracks automatically fallback to bespoke anatomical creature rigs, preventing static T-pose artifacts.
- **Depth-Tested Nameplates & Status Reticles**: Monster nameplates, status badges (`💤 Sleep`, `⚠ Fleeing`, `🌀 Confused`), and target reticles enforce strict depth buffer testing and LOS gating so creature positions are never spoiled through solid walls.
- **Living Visual Behaviors**: Monsters smoothly interpolate across tiles, turn to face you when adjacent, track their health with color-coded 3D nameplates, and play custom Idle/Walk animations.

### ✨ 4. Animated 3D Item Pickups & Kinetic VFX
- **3D Ground Pickups**: Rendered 3D objects with continuous gentle bobbing, slow ambient rotation, and emissive color accents matching Angband item qualities.
- **Kinetic Combat Juice**: Floating damage numbers, critical strike popups, directional hit sparks, and camera trauma screen-shake.
- **Active Spell Projectiles**: Glowing 3D kinetic projectiles with particle trails for player spellcasting, monster breath weapons, and ranged missile attacks.

### 🏰 5. Procedural PBR Masonry, Depth Biomes & Solid Barrier Walls
- **Multi-Octave PBR Materials**: Ashlar limestone masonry, weathered flagstone floors, cavern ceilings, glowing magma veins, and crystalline quartz seams with normal maps and calibrated roughness.
- **6 Subterranean Depth Biomes**: Town & Overworld (0), Upper Crypts (1-15), Overgrown Catacombs (16-35), Crystal Caverns (36-60), Magma Underworld (61-85), and Abyssal Throne (86-100+).
- **Wall Neighbor Discovery & Void-Free Boundary Rock**: Solid walls adjacent to illuminated rooms or hallways render immediately with inherited lighting upon entrance, and boundary rock is synthesized into dark barriers to eliminate see-through void gaps.
- **Corridor Wall Sconces & Clutter**: Deterministic wall torches with point lights every 6–8 tiles, room corner pillars, and dungeon clutter.
- **Atmospheric Lighting**: Dynamic torchlight with realistic sinusoidal flicker, ember particles, volumetric fog, and ambient SSAO.

### ⚰️ 6. Atmospheric Death & Post-Mortem Revelation Experience
- **Solemn Funeral Bells & Atmospheric Memorial Screen**: Custom death audio chimes and a dedicated death UI honoring your fallen hero.
- **Full Runes & Item Disclosure**: Optional full identification unmasks unknown runes, history, and properties across Equipment, Backpack, and Quiver.
- **Authoritative High Scores**: Seamlessly records high scores into Angband's binary score ledger.
- **Quick Restart & Reload**: Single-click actions to reload the latest save, roll a new character, or review character info.

### 🗺️ 7. Dual-Mode Minimap & Independent Geometry Scaling
- **Decoupled Minimap Controls**: Adjust physical HUD window dimensions (`Ctrl+PgUp/PgDn` or `[`/`]`) independently from grid tile zoom radius (`PgUp/PgDn` or `+`/`-`).
- **Full-Screen 2D Tactical Map**: Press `Shift-M` for an instant top-down view with your directional vision cone and fog of memory.

### 🔊 8. Zero-Latency Positional 3D Audio
- **Procedural 16-bit PCM Audio Engine**: Generates spatialized footsteps (stone vs outdoor terrain), blade clangs, critical slashes, spell zaps, door creaks, funeral death tolls, and staircase transitions dynamically with zero external audio assets.
- **Pre-Allocated Sound Pools**: Zero-allocation audio playback eliminates runtime garbage collection hiccups.

### 📜 9. 100% Faithful Angband 4.2.6 Engine Depth
- **Zero Compromises on Roguelike Depth**: Every item, artifact, ego-type, spell, monster AI behavior, and dungeon generator is running directly from unmodified Angband 4.2.6 C code.
- **Seamless Terminal Overlay**: Inventory, stores, character creation, targeting, and wizard debug menus pop up seamlessly via the terminal overlay without breaking immersion or game state.

### 📦 10. Standalone Zero-Install Packaging
- **One-Click Distribution**: Easily packaged via `package.cmd` into `Angband3D-Windows-x64.zip` containing the standalone `Angband3D.exe`, `.pck` assets, and native C binaries. End-users require zero prerequisites.

---

## 📋 Release Notes & Changelog

See **[CHANGELOG.md](CHANGELOG.md)** for detailed version-by-version release notes.

- **v1.0.0 (Latest)**: Comprehensive Release — Standalone distribution packaging, full visual overhaul with PBR parallax materials, procedural 3D creature tokens and fallback rigs, dynamic racial scaling and viewmodel kinematics, depth biomes with volumetric fog, atmospheric death experience with runes disclosure, procedural 3D audio, and decoupled minimap controls.
- **v0.3.0**: Standalone release bundle, packaging pipeline & executable export.
- **v0.2.0**: GitHub Actions release automation, 3D pickup models, combat feedback juice, and expanded smoke tests.
- **v0.1.0**: Initial working prototype of the C JSON bridge and Godot 4 3D client.

---

## How it works

Angband's game logic is well separated from its display layer, and adding a new
front end is a supported extension point. So instead of rewriting the game we
add one:

```
  engine/  (forked Angband)            client/  (Godot 4 C#)
  +-------------------------+          +--------------------+
  |  Angband, unmodified    |  stdout  |                    |
  |                         | -------> |  reads JSON frames |
  |  src/main-bridge.c      |          |  renders 3D world  |
  |    a Term that emits    | <------- |  sends keypresses  |
  |    JSON instead of text |  stdin   |                    |
  +-------------------------+          +--------------------+
```

The bridge publishes two channels every time the game waits for input:

- **Structured** — player state, stats, height/weight, equipment, terrain, visible monsters and objects, messages, light radius. This drives the 3D world.
- **Raw terminal** — the 80x24 character grid. Angband's prompts, menus, character creation, and stores are entangled with its game logic, so they are shown as a text overlay.

Input is delivered as *keypresses*, not as game commands. That means character creation, inventory, stores, targeting, saving, and wizard mode all work through the bridge with no special casing.

## Design rules

1. **The engine fork stays rebasable.** Bridge code lives in new files. Only four existing files are touched, and only to register the front end. We can pull upstream Angband releases indefinitely.
2. **The game is never modified.** Saves written through the bridge are ordinary, binary-compatible Angband saves.
3. **The protocol is versioned** so alternate clients (VR, web, mobile) stay possible.

## Playing

Double-click **`play.cmd`**, or run from PowerShell:

```powershell
.\play.cmd
```

It launches the engine, rolls a character (or lets you choose via the menu), and drops you into the town.

### Controls

| Key | Action |
|---|---|
| **Arrow Left / Right** | Turn camera 90° left / right (instant, costs 0 game turns) |
| **Arrow Up / Down** | Step forward / backward in current camera facing |
| **Numpad 8 / 2** | Step forward / backward relative to camera facing |
| **Numpad 4 / 6** | Strafe left / strafe right relative to camera facing |
| **Numpad 7 / 9** | Diagonal step forward-left / forward-right |
| **Numpad 1 / 3** | Diagonal step backward-left / backward-right |
| **Numpad 5** | Stay in place / rest for 1 turn |
| **`hjkl` / `yubn`** | Classic roguelike cardinal & diagonal grid moves |
| **`>` / `<`** | Descend / ascend stairs |
| **`Shift-M`** | Toggle full-level 2D tactical map overlay |
| **`[` / `]`** *(or `Ctrl+PgUp/PgDn`)* | Scale physical HUD minimap window size |
| **`+` / `-`** *(or `PgUp/PgDn`)* | Zoom HUD minimap grid tile radius |
| **`Tab`** | Toggle raw 80x24 terminal view |
| **`Escape`** | In-game pause menu (Resume, Quick Save, Load, Save & Quit) |
| **`i` / `e` / `w`** | Inventory / equipment / wield item |
| **`d` / `k`** | Drop / destroy item |
| **`m` / `p`** | Cast spell / pray |
| **`Ctrl-S`** | Quick save |
| **`Ctrl-X`** | Save and quit |
| **`Ctrl-W`** | Toggle Wizard (God) mode |
| **`Ctrl-A`** | Open Wizard debug command menu |
| **`?`** | Help |

Prompts, menus, and stores appear in the **terminal view** (`Tab`) — those parts of Angband are tightly entangled with game logic, so they are shown seamlessly as text until native panels replace them.

Squares you can currently see are drawn fully lit; squares you remember but cannot see are dimmed. That distinction comes straight from Angband and drives the fog-of-memory rendering in 3D.

To play plain text Angband instead:

```powershell
.\play.cmd -Classic
```

The `.cmd` wrappers exist because PowerShell restricts running unsigned scripts by default; they bypass that for the one script rather than changing system-wide policy. If you would rather run the `.ps1` files directly, use `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.

## Quickstart for Contributors

We welcome contributions of all kinds — from adding new 3D creature models, dungeon materials, and audio effects to refining UI overlays, optimizing mesh pipelines, or writing alternate frontends (VR, web, mobile).

### Prerequisites

| Tool | Purpose | Recommended Version |
|---|---|---|
| **.NET SDK** | Godot C# client compilation | .NET 8.0 or 9.0 |
| **Godot Engine (.NET version)** | Client editor and runtime | Godot 4.3+ (.NET / Mono) |
| **Python** | Smoke test runner & bridge scripting | Python 3.8+ |
| **MSYS2 / MinGW-w64** *(Windows only)* | Building Angband C engine | GCC 13+, CMake 3.20+, Ninja |
| **GCC / Clang + CMake + Ninja** *(Linux / macOS)* | Building Angband C engine | Standard dev packages |

### Building

```powershell
# 1. One-time toolchain setup (Windows MSYS2 + MinGW)
.\tools\bootstrap.ps1

# 2. Build the Angband C engine
.\build.cmd

# 3. Build the Godot C# client
dotnet build client/angband3d.csproj

# 4. Run automated bridge acceptance tests
python tools/smoke_test.py
```

### Packaging Standalone Releases

To build a clean, self-contained standalone distribution bundle (`Angband3D-Windows-x64.zip`):

```powershell
# Double-click or run from terminal:
.\package.cmd
```

This compiles the engine, exports the standalone release executable (`Angband3D.exe`), packages gamedata and scripts, creates `dist/Angband3D-Windows-x64/`, and compresses it into an optimized `.zip` archive. End-users can extract and double-click `Angband3D.exe` (or `Play-Angband3D.cmd`) to play immediately with zero Godot installation required.

### Extending & Contributing

- **Adding 3D Monster Models**: Map GLTF/GLB models or procedural tokens declaratively in `client/scripts/MonsterModelResolver.cs`.
- **Adding 3D Item Pickups**: Register item meshes and scale in `client/scripts/ItemModelResolver.cs`.
- **First-Person Viewmodel & Rigs**: Customize weapon attachments and hand animations in `client/scripts/ViewModel.cs`.
- **Procedural Masonry & Textures**: Customize PBR shaders, biomes, and geometry in `client/scripts/DungeonWorld.cs`.
- **HUD & UI**: Enhance the 2D canvas and terminal overlays in `client/scripts/Overlay.cs`.
- **Audio & Sound Effects**: Add or tune procedural sound synthesis in `client/scripts/AudioManager.cs`.
- **Engine Bridge**: Maintain the lightweight C JSON bridge in `engine/src/main-bridge.c`.

For comprehensive developer guides, architecture deep dives, and step-by-step tutorials, see:
- 📖 [Contributing Guide](docs/CONTRIBUTING.md) — Step-by-step contribution recipes, coding standards, and PR instructions.
- 🏛️ [Architecture](docs/ARCHITECTURE.md) — Technical rationale, frame synchronization, and rendering pipeline.
- 📡 [Bridge Protocol v1](docs/PROTOCOL.md) — Wire specification for the JSON IPC protocol.
- 🎨 [Graphics & Assets Handover](docs/GRAPHICS_HANDOVER.md) — 3D pipeline blueprints, shaders, and lighting design.
- 🤖 [LLM / Agent Context](docs/LLM_CONTEXT.md) — High-density cheatsheet for AI coding assistants.

## Trying the bridge by hand

```powershell
python tools/bridge.py enter enter '@' '@' '@' '@'
```

Rolls a random character and prints the screen after each keypress. For interactive debugging, see [docs/CONTRIBUTING.md](docs/CONTRIBUTING.md#debugging-the-bridge-by-hand).

## Licence

GPL v2. angband3d is a derivative work of Angband, which is dual licensed under
the GPL v2 and the traditional Angband licence; this project takes the GPL v2
option. See [LICENSE](LICENSE) and `engine/docs/copying.rst`.

Angband's content draws heavily on Tolkien. That is fine for a free,
non-commercial project, but see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
before distributing this in any form that accepts money.
