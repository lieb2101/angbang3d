# Graphics, Assets & 3D Rendering Handover

**Date**: 2026-09-09  
**Status**: Smooth monster entity tracking & orientation, spectral materials, specialized non-humanoid 3D creature tokens, solid block masonry, character model/animation resolution, lava blowout fixes, town building facades, orientation-aware doorways, procedural normal mapping, lighting, item/monster resolvers, and asset pipelines complete and verified.

---

## 1. Summary of Current State & Accomplishments

### A. Persistent Entity Tracking & Monster Movement Smoothing (`client/scripts/DungeonWorld.cs`, `MonsterModelResolver.cs`)
- **Turn-Less Free Camera & Monster Orientation**:
  - Replaced indiscriminate per-frame monster node destruction with persistent `MonsterEntity` tracking. Free camera turning and status updates no longer cause monsters to spin or rebuild erratically.
  - Monsters preserve their identity with engine instance IDs (`id`) and interpolate position (`Lerp`) smoothly along their movement vectors between game turns.
  - Teleportation / blinking (such as thief theft `EAT_ITEM` blink or phase door) immediately snaps positions, preventing unwanted interpolation across player squares.
  - Movement vectors update monster target facing (`TargetYaw`). Monsters play their `Walk` animations when moving between cells and smoothly face the player when adjacent/idle.
  - Continuous vertical bobbing/floating offset calculation for flying, incorporeal, and hovering entities.
  - Overhead 3D nameplates and color-coded health brackets dynamically positioned above character heads with bottom vertical alignment and priority depth-testing to prevent mesh clipping.

### B. Accurate 3D Model Mappings & Creature Resolvers (`client/scripts/MonsterModelResolver.cs`)
- **Glyph-First Monster Archetype Taxonomy**:
  - Primary resolution is strictly categorized by Angband monster glyph (`glyph`) first, eliminating false substring matches (e.g. non-humanoid creatures with "giant" in their name like "giant white mouse" or "giant red ant" previously triggering the Giant Barbarian humanoid model).
  - Rodents (`r` - mice, rats, giant white mouse), Bats/Birds (`b`, `B`), Insects/Arachnids (`a`, `c`, `S`, `K`, `I`, `F`), Reptiles/Canines/Felines/Quadrupeds (`R`, `J`, `C`, `f`, `q`, `Z`), and Slimes/Worms (`j`, `w`, `i`) resolve to dedicated procedural 3D tokens with accurate scale and feature anatomy.
  - Giant humanoid models (`P` - Hill, Frost, Fire, Stone, Cloud, Storm giants, Titan, Cyclops, Morgoth) exclusively map to the imposing `Barbarian.glb` ($1.75\text{--}2.0\times$).
- **Character Skin Tinting & Spectral Material Preservation**:
  - Preserves base albedo diffuse textures across all character GLBs (`Knight`, `Barbarian`, `Mage`, `Rogue`, `Rogue_Hooded`, `Skeleton_*`) while modulating skin, cloth, and armor tones based on the creature's Angband color (`attr` / `Color`).
  - Ethereal entities (`G`, `W`) preserve underlying character texture maps with translucent alpha shaders and spectral colored emission glow.
  - Demons (`U`, `u`) and Maiar/Ainur (`A`) feature fiery or radiant celestial emission maps.
- **Humanoid & Archetype Model Distribution**:
  - Skeletons, Liches, Vampires, and Zombies map to distinct class gear: `Skeleton_Warrior`, `Skeleton_Mage`, `Skeleton_Rogue`, `Skeleton_Minion`.
  - Townspeople (`t`), adventurers, warriors, paladins, rogues, rangers, mages, clerics, barbarians, and mercenaries mapped with scale adjustments (e.g. Halflings/Dwarves $0.65\times$ vs Elves/Humans $0.98\text{--}1.05\times$).
  - Giants, Balrogs, and Trolls scaled with imposing proportions ($1.3\text{--}1.75\times$).
- **Specialized 3D Procedural Creature Tokens**:
  - **Rodents (`r`)**: Compact rodent torso ($0.5\times$ scale), snout, ear meshes, fur modulated to creature color (e.g. white for giant white mouse), and glowing albino red/creature eyes.
  - **Bats & Birds (`b`, `B`)**: Airborne hovering tokens with angled wing spans and glowing eyes.
  - **Floating Eye (`e`)**: 3D sclera sphere with illuminated colored iris and dark pupil.
  - **Slimes, Jellies & Worms (`j`, `i`, `w`)**: Translucent gelatinous dome with glowing pulsating inner nucleus.
  - **Molds & Mushrooms (`m`, `,`)**: Procedural mushroom clusters with glowing spore caps.
  - **Arachnids, Centipedes, Insects (`S`, `K`, `a`, `c`, `I`, `F`)**: Multi-segment body shell with glowing multi-eyes.
  - **Canines & Beasts (`C`, `f`, `q`, `Z`, `R`, `J`)**: Four-legged beast geometry with snout and glowing eye slots.
  - **Dragons & Hydras (`d`, `D`, `M`)**: Draconic spire with curved horn crests and blazing eyes.
  - **Elementals & Vortices (`E`, `v`)**: Faceted crystal prism with spinning orbital rings.
  - **Golems (`g`, `X`, `x`)**: Monolithic stone column with glowing runic visor slit.
  - **Quylthulgs (`Q`)**: Pulsating otherworldly hovering orb.

### C. Solid Stone Block Masonry & Architectural Geometry (`client/scripts/DungeonWorld.cs`)
- **Solid Block Primitives**:
  - Solid $2.0 \times 3.0 \times 2.0\text{ m}$ Godot `BoxMesh` primitives spanning seamlessly from ground ($Y=0$) to ceiling ($Y=3.0\text{ m}$) with instances positioned at $Y=1.5\text{ m}$.
  - Outward normals and double-sided rendering prevent backface culling, inverted windings, and hollow wall appearances.
- **Procedural PBR Textures & Normal Maps**:
  - High-resolution procedural masonry textures with per-block hue variation, chisel bevel highlights, stone grain, and deep mortar channels.
  - Normal maps with bevel gradients (`NormalScale = 1.35`) for 3D depth and surface reaction to torchlight.
  - Distinct procedural textures and emissive maps for **Magma** (`Feat.Magma`, `Feat.MagmaK`) and **Quartz** (`Feat.Quartz`, `Feat.QuartzK`) veins.
  - Flagstone floor textures and tiled stone ceilings.
- **Molten Lava Pools (`Feat.Lava`)**:
  - Dedicated incandescent procedural lava texture and emission map with cooling basalt crust.
  - Tuned emission multiplier (`1.35f`) and triplanar UV mapping, eliminating white HDR blowouts.
- **Town Architecture & Outdoor Atmosphere**:
  - Dedicated half-timbered shop facade textures and framing (`_storeMaterial`) for town shops (`Feat.StoreGeneral` through `Feat.Home`).
  - Open twilight sky background and ambient lighting for depth 0 (`_outdoors`).

### D. Orientation-Aware Doorways & Portals
- **Portal Structure**: Composite procedural meshes consisting of stone jamb posts (`X = \pm 0.85\text{ m}`), top stone lintel spanning to the $3.0\text{ m}$ ceiling, and threshold step.
- **Corridor Alignment**: Dynamic orientation detection evaluating adjacent orthogonal squares to orient doorways across corridor paths (North-South vs East-West).
- **Door States**:
  - **Closed**: Sturdy oak plank door panel sealing the doorway, reinforced with top and bottom forged iron straps and handle rings.
  - **Open**: Open doorway archway with an angled ($70^\circ$) swung-open wooden door leaf on heavy iron hinge brackets, making open corridors clearly passable at a glance in 3D.
  - **Broken**: Splintered wooden debris and planks scattered across the floor with a broken remnant clinging to the top hinge.
  - **Floor Underlay**: Automatic floor tile placed beneath all doorway and stair cells.

### E. Items (`ItemModelResolver.cs`) & Atmosphere
- 3D item pickups with gentle hover and rotation animations.
- Godot 4 Volumetric Fog, SSAO, Tonemap (Filmic), warm torch light with soft shadows and ember particles.

---

## 2. File Index & Roles

| Path | Purpose |
|---|---|
| `client/scripts/DungeonWorld.cs` | 3D rendering pipeline, MultiMesh batches, entity tracking, procedural textures/materials, geometry builders, and torch/camera controllers. |
| `client/scripts/MonsterModelResolver.cs` | Resolves monster data to animated 3D GLTF models, spectral shaders, or specialized 3D creature tokens with animation controllers. |
| `client/scripts/ItemModelResolver.cs` | Resolves item kinds to 3D pickup models with hover animations. |
| `client/scripts/Main.cs` | Game lifecycle, menu/terminal routing, input dispatch, and screenshot test hooks. |
| `client/scripts/Overlay.cs` | 2D HUD, mini-map, status bars, and classic terminal rendering. |
| `client/scripts/BridgeClient.cs` | Engine subprocess IPC via standard streams with JSON protocol. |
| `tools/fetch_assets.ps1` | PowerShell script downloading CC0 assets into `client/assets/models/`. |
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

---

## 4. Comprehensive 3D Visual Enhancement Blueprint (Next Steps)

### Phase 1: First-Person Viewmodel & Weapon Dynamics
- **Goal**: Provide physical presence on screen, eliminating the disembodied floating camera.
- **Node**: Create `client/scripts/ViewModel.cs` attached as a child of `DungeonWorld._camera`.
- **Existing Assets in `client/assets/models/`**:
  - `dungeon/torch_lit.gltf.glb` (Torch / Offhand Light)
  - `characters/sword_1handed.gltf` (Melee Blade)
  - `characters/shield_badge.gltf` (Shield)
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

