# Graphics, Assets & 3D Rendering Handover & Architecture

**Status**: Verified & Feature-Complete across all Core 3D Subsystems: First-Person Viewmodel & Hands, Dynamic Character Scaling, Monster Tracking & Procedural Anatomical Tokens, Kinetic Combat VFX, Corridor Clutter & Sconces, Zero-Allocation Positional Audio, and Dual-Mode Minimap Scaling.

---

## 1. Summary of Current State & Accomplishments

### A. Dynamic Character Height & World Scaling Subsystem (`DungeonWorld.cs`, `ViewModel.cs`)
- **Race & Height Life-Scale Translation**:
  - Dynamically calculates player eye height ($0.82\text{m} - 2.45\text{m}$) from Angband's rolled height in inches (`ht` property) and race taxonomy.
  - Halflings/Gnomes (~36-44") gaze up at towering stone arches, doors, and massive monsters from ~0.88m–1.05m eye height.
  - Humans/Elves (~68-76") navigate dungeons from standard 1.62m–1.76m perspective.
  - Half-Trolls/High-Elves (~84-104") loom from 1.95m–2.35m with sweeping sightlines.
- **Perspective FOV & Footstep Pitch Modulation**:
  - Dynamically scales camera FOV ($86^\circ$ for short races down to $82^\circ$ for giant races).
  - Modulates footstep audio pitch and walking bob amplitude based on character mass and stride height.
- **Viewmodel Hand & Weapon Proportion Adaptation**:
  - Hands, sleeves, and weapon reach adapt their screen scale smoothly based on `CurrentHeightRatio`.

### B. First-Person Viewmodel & Animated Hand Rigs (`ViewModel.cs`)
- **Articulated Arm & Hand Mesh**: Modeled low-poly sleeves, metal wrist cuffs/bracers, palms, opposable thumbs, and 4 articulated fingers wrapping held grips.
- **Dynamic Race & Class Skin/Sleeve Tinting**: Hands and sleeves reflect player race skin tones and class robes.
- **Equipped Wieldable Resolution**: Matches swords, daggers, 2H battleaxes, polearms, bows, crossbows, wands, staves, spellbooks, shields, burning torches, or martial bare fists.
- **Kinematic First-Person Dynamics**: Walk bobbing, yaw/pitch inertia sway, melee thrust/slash action tweens, spellcasting surges, and damage recoil.

### C. Persistent Entity Tracking & Monster Movement Smoothing (`DungeonWorld.cs`, `MonsterModelResolver.cs`)
- **Turn-Less Free Camera & Monster Orientation**:
  - Persistent `MonsterEntity` tracking by instance ID (`id`). Free camera turning does not cause monsters to spin or rebuild.
  - Monsters interpolate position smoothly (`Lerp`) between discrete game turns; phase doors / blinks snap instantly.
  - Monsters play `Walk` animations when moving between cells and smoothly face the player when adjacent/idle.
  - Continuous vertical bobbing/floating offset calculation for flying, incorporeal, and hovering entities.
  - Overhead 3D nameplates and color-coded health brackets dynamically positioned above character heads.
- **Role-Accurate Humanoid Equipment**:
  - Guards wield swords and shields; archers draw crossbows; mages hold glowing staves; thieves wield daggers; beggars fight unarmed.
- **Procedural 3D Creature Tokens**:
  - Dedicated anatomical 3D models for rodents, bats, birds, floating eyes, slimes, worms, molds, arachnids, centipedes, dragons, hydras, demons, and elementals with animated body segments, wings, legs, and glowing eyes.

### D. Kinetic Combat VFX & Floating Combat Feedback ("Juice") (`DungeonWorld.cs`)
- **3D Floating Combat Text**: Floating billboard damage numbers (orange), misses (silver), and critical hits (gold).
- **Directional Impact Sparks**: Impact particles match monster blood or elemental type (crimson, acid green, spark yellow, void purple).
- **Death Dissolve VFX**: Slain monsters burst into ethereal dissolve particles and smoke poofs.
- **Camera Screen Trauma**: High-impact strikes and heavy damage trigger decaying screen shake and viewmodel recoil.

### E. Procedural PBR Masonry & Corridor Clutter (`DungeonWorld.cs`, `DungeonClutterResolver.cs`)
- **MultiMesh Batched Draw Pipeline**: High-performance multi-mesh instances for stone walls, flagstone floors, cavern ceilings, magma fissures, quartz veins, and incandescent molten lava.
- **Orientation-Aware Portals**: Dynamic doorway alignment across corridors (closed, $70^\circ$ open, and broken debris states) and town shopfront facades with heraldic bronze plaques.
- **Corridor Wall Sconces & Clutter**: Deterministic wall torches with point lights every 6–8 tiles, room corner pillars, and dungeon clutter.

### F. Zero-Allocation Positional 3D Audio (`AudioManager.cs`)
- **Procedural 16-bit PCM Audio Engine**: Generates spatialized footsteps (stone vs outdoor terrain), blade clangs, critical slashes, spell zaps, door creaks, and staircase transitions dynamically with zero external audio assets.
- **Pre-Allocated Sound Pools**: 8 2D and 12 3D spatial audio players pooled for zero-garbage playback.

### G. Dual-Mode Minimap & Independent Geometry Scaling (`Overlay.cs`)
- **Decoupled Minimap Controls**: Adjust physical HUD window dimensions (`Ctrl+PgUp/PgDn` or `[`/`]`) independently from grid tile zoom radius (`PgUp/PgDn` or `+`/`-`).
- **Full-Screen 2D Tactical Map**: Press `Shift-M` for an instant top-down view with your directional vision cone and fog of memory.
- **PBR Texture & Normal Mapping Upgrades**:
  - High-resolution (512x512) multi-octave fractal noise maps with micro-grit normal mapping and deep Parallax Occlusion Mapping (POM) on stone masonry and flagstone paving.
  - Deep parallax layer stepping (8 to 32 layers) on wall and floor materials giving true 3D tactile depth under dynamic torchlight.
  - Anatomical slenderizing and realistic proportions on character/monster models with rim and metallic shader highlights.

---

## 2. File Index & Roles

| Path | Purpose |
|---|---|
| `client/scripts/DungeonWorld.cs` | 3D rendering pipeline, MultiMesh batches, character height scaling, combat VFX, and camera kinematics. |
| `client/scripts/ViewModel.cs` | First-person articulated hand rig, racial skin/sleeve styling, wielded weapon matching, and action tweens. |
| `client/scripts/MonsterModelResolver.cs` | Resolves monster taxonomy to 3D GLTF models, equipment roles, or specialized procedural anatomical creature tokens. |
| `client/scripts/ItemModelResolver.cs` | Resolves item kinds to 3D pickup models with hover animations. |
| `client/scripts/DungeonClutterResolver.cs` | Generates corridor torch sconces with point lights and room props. |
| `client/scripts/AudioManager.cs` | Positional and procedural sound synthesis and player pooling. |
| `client/scripts/Overlay.cs` | 2D HUD, resizable mini-map, status bars, and classic terminal rendering. |
| `client/scripts/Main.cs` | Game lifecycle, menu/terminal routing, and input dispatch. |
| `client/scripts/BridgeClient.cs` | Engine subprocess IPC via standard streams with JSON protocol. |
| `tools/smoke_test.py` | 11-test suite verifying bridge protocol, save/load, menus, and gameplay. |

---

## 3. Verification & Commands

- **C# Client Build**:
  ```powershell
  dotnet build client/angband3d.csproj
  ```
  *(Result: 0 Warnings, 0 Errors)*

- **Smoke Tests**:
  ```powershell
  python tools/smoke_test.py
  ```
  *(Result: 11 passed, 0 failed)*

- **Run Game**:
  ```powershell
  .\play.cmd
  ```

  - `characters/wand.gltf` (Wand / Staff)
  - `characters/spellbook_closed.gltf` (Magic Tome)
- **Mechanics**:
  1. **Dual-Hand Positioning**:
     - Left Hand: Torch/Shield at `Vector3(-0.35f, -0.28f, -0.55f)`.
     - Right Hand: Weapon/Staff at `Vector3(0.38f, -0.26f, -0.55f)`.
  2. **Procedural Motion & Sway**:
     - Walk-bob sinusoidal oscillation driven by `StepSeconds` movement accumulator.
     - Yaw/Pitch lag inertia: calculate delta camera angle and lerp viewmodel offset with damping.
  3. **Action Tweens**:
     - Melee Attack: Fast forward thrust ($+0.25\text{ m}$ Z-offset) with $-15^\circ$ slash rotation over $0.12\text{ s}$, then return.
     - Spell Cast: Raise wand/tome with a brief emissive glow pulse.
     - Incoming Hit: Brief recoil back toward the camera ($-0.1\text{ m}$).

### Phase 2: Combat VFX, Floating Combat Text & Kinetic "Juice"
- **Goal**: Bring combat feedback from text logs directly into the 3D world.
- **Hook Points**: `Main.OnFrame` $\to$ `DungeonWorld.OnFrame(JsonElement frame)` (carries `messages` array and monster `hp` changes).
- **Components**:
  1. **3D Floating Combat Text**:
     - Instantiate lightweight billboarded `Label3D` instances above target entity heads.
     - Styling: Orange (`#FF8800`) for damage numbers, silver (`#AAAAAA`) for misses, gold (`#FFDD00`) with $1.4\times$ scale punch for critical hits.
     - Tween: Float upward $+0.8\text{ m}$, scale down $1.2\times \to 1.0\times$, fade alpha to 0 over $0.65\text{ s}$, then `QueueFree()`.
  2. **Impact Particles (`CpuParticles3D`)**:
     - Directional impact sparks/blood bursts matching monster attribute color.
     - Monster death: Dissolve particle burst and spectral smoke poof.
  3. **Spell & Missile Projectiles**:
     - Interpolate high-speed particle nodes (`GpuParticles3D` or `CpuParticles3D` with trail) from player camera to target cell upon spell/missile messages (Magic Missile, Fireball, Arrows, Breath attacks).
  4. **Camera Trauma / Impact Shake**:
     - Add a `_trauma` scalar ($0.0 \to 1.0$) in `DungeonWorld.cs` decaying at $1.5\text{/s}$.
     - On player damage: set `_trauma = Mathf.Min(1.0f, _trauma + dmg / maxHp)` and apply rotational noise shake in `_Process`.

### Phase 3: Procedural Dungeon Dressing & Clutter Placement
- **Goal**: Break up uniform corridor and room geometry using existing CC0 assets.
- **Existing Assets in `client/assets/models/`**:
  - `dungeon/torch_mounted.gltf.glb` (Wall Sconces)
  - `dungeon/column.gltf.glb`, `dungeon/pillar.gltf.glb` (Corner Columns)
  - `dungeon/barrel_large.gltf.glb`, `dungeon/barrel_small_stack.gltf.glb` (Barrels)
  - `dungeon/crates_stacked.gltf.glb`, `dungeon/box_stacked.gltf.glb` (Crates)
  - `dungeon/floor_tile_big_grate.gltf.glb` (Floor Grates)
  - `dungeon/banner_shield_red.gltf.glb` (Wall Banners)
  - `props/tree_dead_large.gltf` (Dead Roots / Foliage)
- **Placement Logic (in `DungeonWorld.Rebuild()`)**:
  1. **Wall Torches**: Detect continuous corridor/room wall sections and spawn a `torch_mounted` instance every 6–8 tiles with a low-energy point light ($1.2\text{ energy}$, $4\text{ m}$ radius).
  2. **Corner Pillars**: Detect concave room corners (where two perpendicular walls meet floor) and place `pillar.gltf.glb`.
  3. **Deterministic Scatter**: For passable floor cells with 2 or 3 adjacent walls (corners, alcoves, dead ends), compute `uint hash = Hash(depth, x, y)`. If `hash % 100 < 15`, spawn a random clutter instance (barrels, crates, bones, cobwebs).
  4. **Wall Banners**: In large open rooms with unbroken walls, spawn decorative banners at $Y = 1.8\text{ m}$.

### Phase 4: Depth-Based Biome Themes & Palette Grading
- **Goal**: Visually communicate depth progression through distinct environmental palettes and atmospheric effects.
- **Biome Configuration Matrix**:
  | Depth Range | Biome Name | Wall/Floor Tint | Lighting / Ambient | Fog Density & Color | Particle FX |
  |---|---|---|---|---|---|
  | **0** | Town / Surface | Warm half-timbered stone | Bright twilight ($1.6\text{ energy}$) | $0.004$, Navy `#141E38` | Night breeze |
  | **1–15** | Upper Crypts & Dungeon | Classic grey limestone | Warm torchlight ($2.4\text{ energy}$) | $0.014$, Dark Grey `#08080A` | Dust motes |
  | **16–35** | Overgrown Ruins & Fungi | Mossy greenish stone | Dim green/amber ($2.0\text{ energy}$) | $0.018$, Murky Green `#0A150D` | Floating spores |
  | **36–60** | Crystal Caverns & Flooded Depths | Blue/slate granite | Cool cyan crystal glints ($1.8\text{ energy}$) | $0.022$, Cyan-Dark `#05101A` | Water drips |
  | **61–85** | Magma Underworld | Obsidian / basalt | Incandescent orange glow ($2.6\text{ energy}$) | $0.025$, Embers `#1A0A05` | Rising heat & sparks |
  | **86–100** | Abyssal Throne (Morgoth) | Pitch-black Permarock | Ethereal violet/crimson ($1.4\text{ energy}$) | $0.030$, Deep Violet `#120418` | Void tendrils |
- **Implementation**: On depth change (`_levelKey`), smoothly interpolate `WorldEnvironment` properties (`VolumetricFogDensity`, `VolumetricFogAlbedo`, `AmbientLightColor`) and assign pre-tinted `StandardMaterial3D` sets.

### Phase 5: Modular Town Architecture Overhaul (Depth 0)
- **Goal**: Replace flat store cubes with an authentic medieval settlement.
- **Existing Assets in `client/assets/models/town/`**:
  - Modular buildings, pitched roofs (`building_A.gltf`, `roof_sloped.gltf`), awnings, and streetlamps.
- **Mechanics**:
  - Replace town `Kind.Store` MultiMesh boxes with modular pitched-roof building models.
  - Add wooden shop doors, market stalls, and streetlamp posts.
  - Position animated 3D townspeople NPCs (`t`) wandering outside shop frontages.
  - Directional sunlight/moonlight with real-time shadow projection across the town square.

