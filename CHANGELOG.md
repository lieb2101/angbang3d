# Changelog & Release Notes

All notable changes to `angband3d` are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [v1.0.0] — 2026-09-14

### 🌟 Comprehensive Release & Dark Fantasy Visual Overhaul
The `v1.0.0` milestone transforms Angband3D into a full-featured, standalone first-person dark fantasy dungeon crawler running atop a 100% faithful, unmodified Angband 4.2.6 engine core.

### Added
- **Dynamic Character Height & Racial Scale**:
  - Character race and rolled physical height (`ht`) dynamically modulate camera eye height ($0.70\text{m} - 2.35\text{m}$), field-of-view perspective ($82^\circ - 86^\circ$), and acoustic footstep pitch.
  - Viewmodel hand scale, forearm reach, and weapon proportions scale proportionally to player race.
- **First-Person Viewmodel & Articulated Hand Rigs**:
  - Sculpted 3D player arms featuring tapered cloth sleeves, metallic wrist bracers, articulated palms, opposable thumbs, and four-finger grips.
  - Race-tailored skin tones and class-tailored sleeve fabric shaders.
  - Real-time weapon and shield matching for 1H/2H swords, daggers, battleaxes, polearms, bows, crossbows, wands, staves, spellbooks, torches, or martial bare fists.
  - Natural walk bobbing, inertia yaw/pitch sway, attack slash/thrust kinematics, spellcast surges, and hit recoil.
- **Procedural 3D Creature Tokens & Living Bestiary**:
  - Hundreds of non-humanoid monsters (dragons, hydras, giant spiders, centipedes, beholders, slimes, basilisks, demons, elementals, kobolds, imps, yeeks, yetis) feature procedural anatomical 3D models with undulating segments, skittering legs, flapping wings, glowing irises, and pulsating nuclei.
  - Fallback rigging system to prevent static T-pose artifacts on unrigged humanoid meshes.
  - Humanoid equipment matching (guards hold swords/shields, archers hold crossbows, mages hold glowing staves).
  - Depth-tested nameplates, status badges (`💤 Sleep`, `⚠ Fleeing`, `🌀 Confused`), and target reticles (`[ ⌖ TARGET ⌖ ]`) with line-of-sight gating.
- **Procedural PBR Materials & 6 Subterranean Depth Biomes**:
  - Multi-octave PBR limestone masonry, flagstone floors, cavern ceilings, and glowing magma/crystal veins with calibrated roughness and normal maps.
  - 6 distinct depth zones: Town (0), Upper Crypts (1-15), Overgrown Catacombs (16-35), Crystal Caverns (36-60), Magma Underworld (61-85), and Abyssal Throne (86-100+).
  - Dynamic `WorldEnvironment` with biome-specific volumetric fog, ambient lighting, and environmental particles (dust motes, glowing spores, crystal shimmers, volcanic embers).
  - Solid boundary rock synthesis and wall-neighbor lighting inheritance to eliminate void gaps.
- **Atmospheric Death Experience & Full Post-Mortem Revelation**:
  - Solemn funeral toll audio bells (`PlayerDeath` SFX) and dedicated memorial UI.
  - Full rune and property identification unmasking across Equipment, Backpack Inventory, and Quiver Missiles upon death.
  - Integrated high score recording and one-click quick restart/re-roll actions.
- **Zero-Latency Positional 3D Audio Engine**:
  - Pure procedural 16-bit PCM sound synthesis for spatialized footsteps, weapon impacts, spell zaps, door creaks, funeral death tolls, and staircase transitions.
  - Pre-allocated zero-allocation sound pools.
- **Decoupled Dual-Mode Minimap**:
  - Independent physical HUD window scaling (`Ctrl+PgUp/PgDn` or `[`/`]`) and grid zoom radius (`PgUp/PgDn` or `+`/`-`).
  - Full-screen 2D tactical map overlay (`Shift-M`).
- **Standalone Distribution & Packaging Pipeline**:
  - Automated `package.ps1` script creating clean, self-contained Windows bundles (`Angband3D-Windows-x64.zip`) containing standalone `Angband3D.exe`, `.pck` gamedata, engine binaries, and launcher scripts with zero Godot installation requirement.

### Changed
- Refactored `BridgeClient.cs` to utilize non-blocking async IO streams with robust backpressure handling.
- Optimized multi-mesh batching to render up to 12,000 dungeon tiles in $\le 10$ draw calls.
- Improved directional numpad controls to always align with the camera's current cardinal facing.

---

## [v0.3.0] — 2026-08-28

### Added
- Standalone `Angband3D.exe` export support via Godot headless packaging toolchain.
- `package.cmd` one-click packaging utility for Windows.
- Runtime path discovery for embedded gamedata libraries (`lib/save/`, `lib/scores/`, `lib/user/`).

### Fixed
- Fixed process exit hang when closing the client while engine process is active.
- Fixed terminal overlay focus clipping on wide aspect ratio monitors.

---

## [v0.2.0] — 2026-08-15

### Added
- Automated GitHub Releases workflow in `.github/workflows/release.yml`.
- Reusable packaging scripts and automated MSYS2 MinGW-w64 toolchain bootstrapping (`tools/bootstrap.ps1`).
- Dynamic 3D item pickup models with continuous floating rotation and emissive color accents.
- Screen shake / trauma feedback on physical melee hits.

### Changed
- Standardized documentation structure (`ARCHITECTURE.md`, `PROTOCOL.md`, `CONTRIBUTING.md`).
- Extended smoke test suite with 11 automated acceptance test cases.

---

## [v0.1.0] — 2026-07-20

### Added
- Initial working prototype of `angband3d`.
- High-performance C JSON bridge frontend in `engine/src/main-bridge.c` communicating over stdio.
- Godot 4 (.NET C#) 3D frontend with first-person perspective, multi-mesh wall rendering, and dynamic torchlight.
- Seamless 80x24 terminal overlay for text menus, stores, prompts, and character birth.
- 100% binary savefile compatibility with upstream Angband 4.2.6.
