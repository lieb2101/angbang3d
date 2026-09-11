# Angband3D — Status & Next Steps Roadmap

## Current System State (Post Low-Hanging Fruit & Viewmodel Hand Enhancements)

1. **Engine Bridge**:
   - Upstream Angband 4.2.6 fork on branch `bridge` with `main-bridge.c`.
   - Complete player telemetry: HP, SP, AC, Max/Exp/Next Exp, Gold, Stats (STR/INT/WIS/DEX/CON with reductions), active statuses array, targeting monster tracker, depth/feelings, physical height (`ht`) and weight (`wt`), equipped light source, weapons (`weapon_item`), bows (`bow_item`), and shields (`shield_item`).
   - 11/11 bridge smoke tests passing (`python tools/smoke_test.py`).
   - Patch file `engine-patch/0001-bridge-frontend.patch` fully synchronized.

2. **Godot 4.7.2 C# Client**:
   - **First-Person Viewmodel & Hands (`ViewModel.cs`)**:
     - Tapered forearm sleeves with class-tailored fabric shaders.
     - Modeled wrist cuffs / metal bracers.
     - Articulated palm, opposable thumb, and 4 sculpted fingers with natural grip wraps around wielded weapon and torch handles.
     - Character height & race scale adaptation ($0.70\text{m} - 2.10\text{m}$ eye heights).
     - Reactive camera bob, weapon sway, attack slash/thrust animations, spellcast surges, and hit trauma recoil.
   - **Item & Monster Model Resolution**:
     - Full item matching for swords, daggers, 2H axes, 1H axes, staves, bows, crossbows, shields, books, wands, and torches.
     - Bare hands / martial fists when unarmed.
     - Animated 3D pickups with continuous hover, slow rotation, and emissive color accents.
   - **Audio & Juice Feedback**:
     - Positional sound effects (`AudioManager.cs`) for footsteps, melee impacts, spell zaps, door creaks, and stairs.
     - Floating damage numbers & crits (`Label3D` billboarding), hit sparks, and camera screen-shake.
   - **Dungeon Aesthetics & Clutter**:
     - Deterministic wall sconces with point lights in corridors (`DungeonClutterResolver.cs`).
     - Procedural props and furniture in rooms.

---

## Active Execution Focus & High-Fidelity Roadmap (Daggerfall / Skyrim Aesthetic Track)

The project has achieved the **Tier 4 Visual & Environmental Overhaul**: delivering immersive dungeon atmosphere, depth-based biome shifts, dynamic torch flame VFX, rich PBR materials, and 2.5D normal-mapped monster rendering, while preserving 100% of viewmodel, tracking, and bridge architecture.

### Priority 1: Depth-Based Biomes & Atmospheric Lighting (COMPLETED)
- **Scope**: `client/scripts/DungeonWorld.cs`
- **Accomplishments**:
  - Implemented depth lookup matrix in `DungeonWorld.cs` with `BiomeProfile`.
  - 6 distinct subterranean depth zones: Town & Overworld (0), Upper Crypts (1-15), Overgrown Catacombs (16-35), Crystal Caverns (36-60), Magma Underworld (61-85), and Abyssal Throne (86-100+).
  - Dynamic `WorldEnvironment` properties (`FogDensity`, `FogLightColor`, `AmbientLightColor`, `AmbientLightEnergy`, `TonemapExposure`).
  - Active atmospheric particulate emitter (`CpuParticles3D`) dynamically configured per biome for dust motes, luminous spores, crystal shimmers, and rising volcanic embers.

### Priority 2: High-Fidelity PBR Materials & Normal/Roughness Mapping (COMPLETED)
- **Scope**: `client/scripts/DungeonWorld.cs`
- **Accomplishments**:
  - Procedural PBR masonry with multi-octave normal mapping, chiseled bevels, recessed mortar joints, and wear-modeled flagstone roughness.
  - Incandescent emissive glow for magma fissures, lava flows, and crystal veins with Softlight bloom.
  - Medieval oak plank doors with forged iron reinforcement straps, rivets, and emblazoned shop door numerals.

### Priority 3: Dynamic Torch Flame VFX & Ego Weapon Light Auras (COMPLETED)
- **Scope**: `client/scripts/ViewModel.cs`, `client/scripts/DungeonWorld.cs`
- **Accomplishments**:
  - Animated `CpuParticles3D` torch flame, rising smoke plume, and dynamic tip omni light.
  - Multi-octave Perlin noise light flicker and shadow jitter.
  - Dynamic elemental particle auras and colored lighting for ego-branded weapons (Flame, Frost, Lightning, Acid/Venom, Holy/Slay).

### Priority 4: Viewmodel Glove & Gauntlet Hand Armor Overlays (COMPLETED)
- **Scope**: `client/scripts/ViewModel.cs`
- **Accomplishments**:
  - Integration with engine body armor and glove slots (`body_armor_item`, `gloves_item`).
  - First-person viewmodel displaying held equipment with clean unobstructed lower-corner rest transforms.

### Priority 5: 3D Magic Projectiles & Monster Status VFX (COMPLETED)
- **Scope**: `client/scripts/DungeonWorld.cs`, `client/scripts/MonsterModelResolver.cs`
- **Accomplishments**:
  - Kinetic 3D projectiles (`ActiveProjectile`) with glowing cores and particle trails for arrows, player spells, and enemy spell attacks.
  - Overhead 3D status billboarding for Sleep ("💤 Zzz..."), Fear ("⚠ FLEEING"), Confusion ("🌀 CONFUSED"), and Stun ("💫 STUNNED").
  - Overhead target reticle badge (`[ ⌖ TARGET ⌖ ]`) locked to engine target tracking.

---

## Next Backlog & Future Milestones

1. **Step 12: Ambient Subterranean Soundscapes** (`AudioManager.cs`)
   - Layered depth-based looping ambient audio (dripping water in crypts, cavern wind in catacombs, subterranean rumble in magma depths).
2. **Step 14: Dynamic Door Kinematics & Smashed Debris VFX** (`DungeonWorld.cs`)
   - Animated smooth swing open/close interpolation and splintered wood particle bursts on door smashing.
3. **Packaging & Distribution Verification** (`package.ps1`)
   - Standalone release export validation across Windows targets.


## Secondary Polish Queue (Wave 2 Backlog)

- **Step 13**: Minimap Fog-of-War Smoothing & Golden Discovery Pulses (`Overlay.cs`).
- **Step 12**: Procedural Atmospheric Subterranean Soundscape (`AudioManager.cs`).
- **Step 14**: Dynamic Door Kinematics & Destruction Debris (`DungeonWorld.cs`).

---

## Skyrim-Style Graphical & Model Quality Scaling Track

1. **PBR Surface Realism & Parallax Mapping**:
   - 2K/4K PBR material sets (Albedo, Normal, Roughness, AO, Height/Displacement) for chiseled granite stone walls, damp flagstones, and cavern walls.
   - Parallax Occlusion Mapping (POM) in `StandardMaterial3D` for deep mortar crevices and stone protrusions.
2. **Forward+ Lighting & Atmospheric Post-Processing**:
   - Volumetric Fog with light-shaft scattering for torches and wall sconces.
   - Signed Distance Field Global Illumination (SDFGI) for secondary light bounce.
   - ACES Tonemapping and Nordic/dark-fantasy color grading LUT (cool slate shadows, warm incandescent fire).
   - Perlin-noise torch light jitter and flicker dynamics.
3. **Rigged 3D Assets & Skeletal Animations**:
   - Rigged first-person arm/hand pack with dedicated bone animations (walk, swing, block, shoot, cast).
   - High-fidelity dark-fantasy monster meshes with skeletal movement and combat animations.

---

## Quick Reference Commands

- **Run Smoke Tests**: `python tools/smoke_test.py`
- **Build Client**: `dotnet build client/angband3d.csproj`
- **Play Game**: `.\play.cmd` (or `.\play.ps1 -Character <name> -Random`)
- **Engine Rebuild**:
  ```powershell
  $env:MSYSTEM='MINGW64'; $env:CHERE_INVOKING='1'
  & C:\msys64\usr\bin\bash.exe -lc "cd /c/Dev/angband3d/engine && cmake --build build"
  ```
