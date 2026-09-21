# Angband3D — Status & Next Steps Roadmap

## Current System State (Post Low-Hanging Fruit & Viewmodel Hand Enhancements)

1. **Engine Bridge**:
   - Upstream Angband 4.2.6 fork on branch `bridge` with `main-bridge.c`.
   - Complete player telemetry: HP, SP, AC, Max/Exp/Next Exp, Gold, Stats (STR/INT/WIS/DEX/CON with reductions), active statuses array, targeting monster tracker, depth/feelings, physical height (`ht`) and weight (`wt`), equipped light source, weapons (`weapon_item`), bows (`bow_item`), shields (`shield_item`), cause of death (`died_from`), and fully identified `equipment`, `inventory`, and `quiver` object arrays on death.
   - 11/11 bridge smoke tests passing (`python tools/smoke_test.py`).
   - Patch file `engine-patch/0001-bridge-frontend.patch` fully synchronized.

2. **Godot 4.7.2 C# Client**:
   - **Full-Featured Death Experience & Post-Mortem Disclosure (`Overlay.cs`, `Main.cs`)**:
     - Atmospheric death screen with funeral toll audio bells (`PlayerDeath` SFX).
     - Full item & runes disclosure across Equipment, Backpack Inventory, Quiver Missiles, and Ability Scores.
     - One-click / one-key quick actions: `[R]` Reload last saved game, `[N]` Re-roll new character, `[M / Esc]` Return to main menu.
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
   - **Dark Unmapped Area Sealing & Dynamic Emission Gating (`DungeonWorld.cs`)**:
     - Unmapped rock boundary sealing: tiles with `FEAT_NONE` bordering explored walkable/portal space automatically render as dark solid stone boundary walls, eliminating see-through voids into the unmapped world.
     - Dynamic per-instance emission shader for lava and magma: modulates emission by instance alpha (`COLOR.a`), ensuring full incandescent molten glow in direct line-of-sight while extinguishing emission in player memory or dark areas.
     - Enclosed subterranean lava pools with dungeon ceilings, avoiding black void cutouts.
   - **Option A Cloud Architecture & Dual-Engine Relay (`server/`, `WebSocketBridgeClient.cs`, `Main.cs`)**:
      - Headless Linux multi-stage Docker container (`server/Dockerfile`, `server/docker-compose.yml`) hosting Angband 4.2.6 C engine with Bridge protocol.
      - High-performance WebSocket daemon (`server/src/server.js`) with isolated child process management per session, REST API for save file management (`/api/saves`), and static file delivery.
      - Dual-engine client architecture: unified `IGameEngineBridge` supporting runtime toggling between `Local Engine` (process stdio) and `Cloud Realm` (WebSockets) with roundtrip ping telemetry on the HUD.
      - **Web 3D Graphics 1:1 Parity Overhaul (`dungeon3d.js`, `hud.js`, `input.js`)**:
         - Fixed Three.js `InstancedMesh` zero-albedo bug by pre-initializing `instanceColor` buffers to 1.0, eliminating pitch black dungeon walls.
         - Replicated Godot's `OmniAttenuation = 0.70f` lighting falloff, illuminating dungeon depths authentically.
         - Removed stand-in mannequin block arms and unshaded flame cones. First-person viewmodel now strictly follows Godot `ViewModel.cs` (unobstructed corner-mounted weapons with multi-material PBR).
         - Fixed unhandled viewmodel runtime TypeError that halted web client frame processing.
         - Floating 3D billboard shop signage (`[1] General Store` ... `[8] Home`, `[>] Down to Dungeon (50')`) with crisp black outlines and distance culling matching Godot `Label3D`.
         - 1:1 Godot minimap: direct ASCII glyph parsing from `map.rows[y].g`, `a`, `l` with 32-color palette, LOS darkening, floor underlays, golden vision cone with boundary arc, two-tone directional polygon pointer (`drawDirectionalPointer`), and `MINIMAP (X,Y) Town [▲ N]` coordinate header.
         - Minimap controls: size presets (`compact`, `expanded`, `tactical`), continuous zoom (0.5x to 3.0x), header click cycling, and real-time 60fps rotating compass needle with 8-point text (`N`, `NE`, `E`, `SE`, `S`, `SW`, `W`, `NW`).
         - Detailed 3-row footer replicating Godot `Overlay.cs:1620-1748` (contextual stairs hint, name/race/class/level, colored HP, SP, AC, gold, EXP, place, speed, light status with fuel/radius, facing, target tracking, condition badges).
         - **Camera Orientation & Level Horizon Fix**: Set Three.js Euler rotation order to `'YXZ'` (Yaw first around vertical world axis, then Pitch up/down, then Roll) and clamped stationary roll (`rotation.z`) strictly to 0. Completely eliminated the Dutch-angle left/right room tilt and diagonal skew, providing a level horizon in all directions while keeping character height pitch compensation.
         - **Wall Sealing & Anti-Pop-In Fix**: Added boundary wall inference in `dungeon3d.js` and real-feature serialization in `main-bridge.c`. Unexplored grid cells (`feat === 0`) bordering explored/visible walkable floors automatically render as solid granite walls (`feat = 21`), ensuring dungeon corridors and town facades never show transparent holes into the void before collision.
         - **Atmospheric Death Screen & Full Terminal Interactivity**: Crimson-themed post-mortem modal with 5 tabs (Tombstone & Menus, Equipment, Inventory, Quiver, Stats grid). Tab 0 forwards all terminal keystrokes (`y`, `n`, `Enter`, `Space`, arrow keys) to answer panic save and character dump prompts, while `Tab`/`1`..`5` switch tabs, `u` identifies items, `R` reloads, and `N` rerolls.
         - Deployed and serving on Google Cloud Run (`https://angband3d-cloud-564958309282.us-central1.run.app`).
   - **Universal Save Game Portability & Permadeath Snapshots (`Main.cs`, `Overlay.cs`)**:
      - In-game `Save Game Manager` menu: archive saves to timestamped backups (`lib/save/backups/`), export `.sav` files directly to user Downloads, and restore backups with `SaveVNLA` binary validation.
      - Bi-directional Cloud Sync: Upload local characters to cloud server and synchronize cloud characters down to local disk.
      - In-game Standalone Package Download: Menu action to download the offline game bundle (`.zip`) directly from within the game.
   - **Graphics & Spatial Occlusion Culling (`DungeonWorld.cs`)**:
      - Radial horizon culling ($R \le 28$ tiles) eliminating instance buffer updates outside the maximum visible fog horizon.

### Priority 6: Deep Efficiency, Performance, Operations & Standalone Packaging (COMPLETED)
- **Scope**: `server/src/server.js`, `server/public/js/dungeon3d.js`, `tools/package.ps1`, `docs/SYSTEM_DESIGN.md`
- **Accomplishments**:
  - **Zero-Allocation 3D Web Rendering**: Pre-allocated scratch color vectors and reusable math objects in `dungeon3d.js` eliminating ~15,000 heap allocations per map frame and abolishing garbage collection stutter.
  - **Cloud Delivery & Network Bandwidth Optimization**: Streaming native gzip/deflate compression in `server.js` reducing client bundle (`dungeon3d.js`) from 186KB down to 37.8KB (>79% compression ratio).
  - **Production Caching & Liveness Probes**: Implemented `Cache-Control: public, max-age=86400, immutable` for static 3D models/textures, and `/health` reporting uptime, active session counts, and engine binary status.
  - **Process Lifecycle & Graceful Shutdown**: Track active sessions in an in-memory Map and hook `SIGTERM`/`SIGINT` to cleanly flush saves and terminate engine child processes.
  - **Hardened Windows Packaging Pipeline**: `tools/package.ps1` bundles Godot's C# .NET assembly directory (`data_angband3d_windows_x86_64`), compiled C engine, PCK, launchers, and generates `Angband3D-Windows-x64.zip` and canonical `angband3d-standalone.zip`.
  - **Authoritative System Architecture Documentation**: Authored `docs/SYSTEM_DESIGN.md` with complete ASCII/Mermaid topologies, zero-alloc bridge specifications, coordinate invariants, and operational runbooks.
  - **Verification**: 11/11 C smoke tests, 0 warnings dotnet build, 7/7 server unit tests, and automated headless Chrome test verifying level horizon camera Euler order and death modal input handling.

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

### Priority 7: High-Fidelity Procedural Audio Synthesizer & Contextual Sfx Overhaul (COMPLETED)
- **Scope**: `server/public/js/audio.js`, `server/public/js/dungeon3d.js`, `server/public/js/input.js`
- **User Constraint Invariants**:
  - Strictly **NO ambient looping noise** (no droning wind or hums).
  - 100% useful action, combat, interaction, and status indication sound effects.
  - Pure client-side Web Audio API synthesis (zero network asset downloads, zero 404s, zero latency).
- **Accomplishments**:
  - **Master Dynamics Limiter**: Inserted a `DynamicsCompressorNode` (`-12dB` threshold, `12dB` knee, `4.5` ratio, `3ms` attack, `120ms` release) on the master bus, guaranteeing zero digital clipping when multiple strikes, spells, footsteps, and creature acoustics play concurrently.
  - **Acoustic Physical Models**:
    - `hit`: Inharmonic Euler-Bernoulli bar mode frequencies ($f_0=460\text{Hz}$, $2.76f_0$, $5.40f_0$, $8.93f_0$) + low-end punch transient with soft non-linear saturation (`Math.tanh`).
    - `crit`: Sub-bass 75Hz drop + heavy hammer impact transient + ringing anvil overtone sustain.
    - `goldPickup`: Rapid 3-coin cascade (distinct impacts at $0\text{ms}$, $42\text{ms}$, $88\text{ms}$ with crystal overtone pings at 2093Hz, 2349Hz, 2793Hz).
    - `shieldBlock` / `armorDeflect`: Resonant 580Hz/1160Hz clang + high ricochet ping at 2800Hz.
    - `wallBump`: Dull 68Hz stone impact punch + surface grit friction crunch for impassable walls/doors.
    - `quaff`: Liquid bottle uncork/pop transient + two resonant bubble gulps (480Hz & 580Hz).
    - `scroll`: Fibrous parchment texture noise + glowing mystical triad chime (C5, G5, E6).
    - `eat`: Crispy multi-bite ration crunch with teeth click.
    - `chestOpen`: Heavy creaking wooden lid friction + iron latch snap.
    - `trapDisarm` & `trapTrigger`: Delicate clockwork release + relief chime vs sudden spring snap + danger thud.
    - `teleport`: Exponential spatial frequency warp (180Hz to 1400Hz) + vacuum pop.
  - **Elemental Spells**: Dedicated synthesis profiles for `fire` (combustion blast + crackle), `cold`/`frost` (crystalline ice shatter), `lightning` (electric arc snap + thunder roll), `poison` (caustic sizzle + bubble), and `magic` (ethereal harmonic sweep).
  - **Creature Vocalizations & Grunts by Glyph**:
    - Canines (`C`, `Z`, `d`): Guttural rasping growl (`synthMonsterGrowl`).
    - Serpents/Reptiles (`J`, `n`, `R`): Sibilant venomous rattle and sharp hiss (`synthMonsterHiss`).
    - Undead/Wraiths (`G`, `W`, `L`, `v`): Chilling spectral harmonic wail (`synthGhostWail`).
    - Dragons/Demons (`D`, `U`, `B`): Immense subterranean sub-bass roar (`synthDragonRoar`).
    - Rodents/Bats (`r`, `b`): High-pitch double chirp (`synthRodentSqueak`).
    - Insects/Spiders (`s`, `S`, `I`): Multi-click chitinous mandible snaps (`synthInsectChitin`).
  - **Status Condition Warning Indicators**:
    - Immediate synthesized acoustic cues with intelligent re-trigger throttling for `poison`, `confused`, `blind`, `paralyzed`, `afraid`, and `hunger`.
  - **3D Positional Stereo Panning**:
    - Implemented `calculateStereoPan(worldX, worldZ)` projecting relative monster coordinates onto camera right-vector for dynamic binaural spatial panning in Web Audio `StereoPannerNode`.
  - **Full C# Godot Parity (`AudioManager.cs`, `DungeonWorld.cs`)**:
    - Ported all 28 acoustic physical sound effect models into C# 16-bit PCM dynamic synthesizer in `AudioManager.cs`.
    - Wired status condition warnings, monster family vocalizations, and combat message hooks into `DungeonWorld.cs`.
  - **Perpetual Panic Save Trap Elimination & State Persistence Overhaul**:
    - Added `save` command to engine bridge (`main-bridge.c`), invoking `savefile_save(savefile)` for clean disk persistence with `player->is_dead = false`.
    - Automated deletion of stale panic saves in `init_bridge` when `-n` is passed so fresh games never prompt `"A panic save exists. Use it?"`.
    - In `Main.cs`: `StartGame(newCharacter: true)` passes `-n` and generates isolated character slots (`Adventurer_xxxx`), preventing save collisions with OS username.
    - In `server.js`: Clean save flush on disconnect (`ws.on('close')`) enables seamless reconnection without panic prompts; `new=1` query param purges any stale panic files and passes `-n`.
    - Cache-busting (`?v=2.2`) and `must-revalidate` headers prevent browser disk caching of old audio/engine scripts.
- **Verification**: 11/11 bridge smoke tests passed, 55/55 synthesis method tests passed, 31/31 Chrome Web Audio in-browser headless tests passed with master compressor active, and automated persistence/reroll integration test passed without panic prompts.

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
