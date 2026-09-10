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

The project focus has pivoted directly to **Tier 4 Visual & Environmental Overhaul**: delivering immersive dungeon atmosphere, depth-based biome shifts, dynamic torch flame VFX, and rich PBR materials, while preserving 100% of existing viewmodel, tracking, and bridge architecture.

### Priority 1: Depth-Based Biomes & Atmospheric Lighting (Phase 4 / Tier 4)
- **Scope**: `client/scripts/DungeonWorld.cs`
- **Objective**: Transform uniform grey limestone into 6 distinct atmospheric subterranean depth zones that smoothly transition as the player descends:
  1. **Town / Overworld (Depth 0)**: Midnight sky, cool navy fog, moonlight directional shadows, warm streetlights.
  2. **Upper Crypts (Levels 1–15)**: Cold ashlar limestone, warm 2400K torchlight, faint dust motes.
  3. **Overgrown Catacombs (Levels 16–35)**: Damp mossy green stone, murky green volumetric fog, floating luminous spores.
  4. **Crystal Caverns (Levels 36–60)**: Blue slate granite, cyan crystal glints, damp reflective floor flagstones.
  5. **Magma Underworld (Levels 61–85)**: Charcoal obsidian walls, glowing magma rivers with orange bloom, rising embers and heat distortion.
  6. **Permarock / Abyssal Throne (Levels 86–100)**: Pitch-black monolithic permarock, void purple fog, crimson accents.
- **Key Tasks**:
  - Implement a depth lookup matrix in `DungeonWorld.cs` returning `BiomeProfile`.
  - On level transition (`_levelKey` change), smoothly lerp `WorldEnvironment` properties (`VolumetricFogDensity`, `VolumetricFogAlbedo`, `AmbientLightColor`, `AmbientLightEnergy`, `TonemapExposure`).
  - Swap / tint MultiMesh materials for walls, floors, and ceilings per biome.
  - Dynamically configure atmospheric particulate emitter (dust motes, spores, mist drips, rising embers).

### Priority 2: High-Fidelity PBR Materials & Normal/Roughness Mapping
- **Scope**: `client/scripts/DungeonWorld.cs`
- **Objective**: Elevate procedural stone surfaces with tactile normal maps, deep mortar crevices, wet roughness variation, and glowing mineral veins.
- **Key Tasks**:
  - Multi-octave procedural normal mapping for chiseled masonry, flagstone slabs, and heavy wooden door planks.
  - Specular glints and roughness maps that react dynamically to moving torchlight.
  - Incandescent emissive glow for magma fissures and crystal veins with Softlight bloom.

### Priority 3: Dynamic Torch Flame VFX & Ego Weapon Light Auras (Step 9)
- **Scope**: `client/scripts/ViewModel.cs`, `client/scripts/DungeonWorld.cs`
- **Objective**: Bring held light sources and enchanted weaponry to life with particle fire and ambient flicker.
- **Key Tasks**:
  - Replace static torch tip with animated `CpuParticles3D` flame and rising smoke plume.
  - Multi-octave Perlin noise light flicker (subtle position jitter + luminous intensity pulse) casting dancing shadows.
  - Elemental particle auras for ego-branded weapons (Flame, Frost, Lightning, Venom) and pulsing runes on spellbooks.

### Priority 4: Viewmodel Glove & Gauntlet Hand Armor Overlays (Step 11)
- **Scope**: `client/scripts/ViewModel.cs`
- **Objective**: Match first-person hand geometry to equipped body and hand armor.
- **Key Tasks**:
  - Dynamic hand overlays: Bare hands $\to$ Leather wraps $\to$ Studded bracers $\to$ Heavy steel plate gauntlets.

### Priority 5: 3D Magic Projectiles & Monster Status VFX (Step 10 & Spells)
- **Scope**: `client/scripts/DungeonWorld.cs`
- **Objective**: Kinetic in-world spells and monster behavioral cues.
- **Key Tasks**:
  - Visual projectile trails (Magic Missile, Fireball, Arrows) streaking from camera to target coordinates.
  - Overhead 3D status billboarding ("Zzz" sleep indicators, panic/sweat cues, targeting reticle badge).

---

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
