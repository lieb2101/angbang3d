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
         - **True Fog of War & Misty Blackness**: Replaced corridor boundary wall synthesis with authentic subterranean darkness. Unexplored cells (`feat === 0 || (!known && !inView)`) are not rendered as fake granite walls, allowing dungeon corridors to open into smooth, atmospheric `THREE.FogExp2` misty blackness.
         - **Reliable `-more-` Prompt Dismissal**: Added direct `key space` handling for <kbd>Space</kbd>, <kbd>Enter</kbd>, and click events on `#prompt-bar` / exploration canvas during `ui.more` states, preventing melee attack animations and ensuring instant prompt advancement.
         - **Atmospheric Death Screen & Full Terminal Interactivity**: Crimson-themed post-mortem modal with 5 tabs (Tombstone & Menus, Equipment, Inventory, Quiver, Stats grid). Tab 0 forwards all terminal keystrokes (`y`, `n`, `Enter`, `Space`, arrow keys) to answer panic save and character dump prompts, while `Tab`/`1`..`5` switch tabs, `u` identifies items, `R` reloads, and `N` rerolls.
      - **Web Client Usability, Interaction & Explicit Controls Milestone**:
         - **Seamless Character Confirmation -> 3D Transition**: Fixed terminal lock bug after character creation by clearing `forceTerminal = false` and dismissing the terminal on `Accept & Play (Enter)` button click and physical <kbd>Enter</kbd>/<kbd>y</kbd> keys. Added automatic phase transition guard in `network.onFrame` and restricted review toolbar strictly to `!frame.map`.
         - **Real-Time Message Feed Queue (Newest First & Turn Fading)**: Inverted message order so that the latest message is always displayed at the top (`#message-log-queue.prepend`), popping in with spring animation (`@keyframes msgPopIn`). Stripped trailing prompts (` -more-`, ` [more]`) from log entries. Messages smoothly fade after 2 turns (or 5.5s) and are cleanly evicted after 4 turns (or 7.5s) while cascading down with decreasing opacity.
         - **Comprehensive 3D Item Model Resolution & Footwear Parity**: Overhauled `createItem3DEntity` with full keyword and glyph resolution matching Godot's `ItemModelResolver.cs`. Replaced generic white octahedron placeholders with full OBJ templates (Armor, Helmets/Crowns, Gloves, Shields, Axes, Hammers, Greatswords, Daggers, Spears, Bows, Arrows, Food, Gems, Skulls, Keys, Backpacks/Pouches) and dedicated procedural 3D meshes (leather soles & straps for sandals/boots/shoes `]`, glowing torches `~`, toruses for rings `=`, gem pendants for amulets `"`, and potions `!`).
         - **Non-Intrusive Glassmorphic Footer Overhaul**: Replaced the bulky 160px, 3-row monospace text block with a compact 28px glassmorphic status ticker (`.hud-status-bar`). Organized telemetry into environment pills (`[🏛 Town]`, `[🧭 N]`, `[☀️ Daylight]`, `[⚡ Spd Normal]`), dynamic contextual alerts (`[⬇ Downstairs >]`, `[⚔ Target]`, `[⚔ Weapon]`), and condition badges (`[Fed]`, `[Study]`, `[⌨ Controls ?]`), completely freeing the 3D viewport.
         - **Random Character Review Pause**: Auto-birth halts on the final character sheet, giving players full review of rolled stats, race, class, and history before entering town, with `⚔ Accept & Play (Enter)` and `🎲 Reroll Hero (R)` buttons.
          - **Store Entrance Auto-Display & Native Trigger Parity**: Fixed shop entrance navigation by removing erroneous artificial key injection on shop entrance tiles. Stepping onto a shop entrance tile automatically invokes `EVENT_ENTER_STORE` (`overlay: 2`), opening the store interface with store owner/title and inventory. Exiting returns directly to the 3D town world.
          - **Contextual Footer Action Menus & Touch Pills**: Fixed `isStore` misclassification in `updateTerminalToolbar`. Opening Inventory (`i`), Equipment (`e`), Throw (`v`), Quaff (`q`), Read (`r`), Cast (`m`), Fire (`f`), or Drop (`d`) in town or dungeon opens the contextual terminal modal with interactive item pills (`[a] Item Name`, `[b] Item Name`), dynamic titles (`🎒 INVENTORY PACK`, `🛡 EQUIPPED GEAR`, `🎯 THROW ITEM`, etc.), switch (`/`), and cancel (`Esc`) returning smoothly to 3D.
          - **Comprehensive Message Feed & Real-Time Action Log**: Eliminated dropped combat messages by removing consecutive identical message suppression (`prevLast`). Added real-time capture from `frame.term.rows[0]` for single-turn action notices, warnings, and failures (e.g. `"There is a wall in the way!"`, `"You have no potions from which to quaff."`, `"You have nothing to fire with."`, `"You see nothing there to open."`). Color-coded lines by event type (red for monster damage, gold for player attacks/kills, green for healing, blue for spells, orange for warnings).
         - **Staircase Descent Phase Contract**: Fixed `phase` serialization in `main-bridge.c` from `bridge_in_play() ? "play" : "setup"` to `character_generated ? "play" : "setup"`, preventing the character creation toolbar from appearing during level generation.
         - **Physical Escape Key**: Physical <kbd>Escape</kbd> now forwards to the engine across all menus, stores, and terminals, matching the on-screen `[Back (Esc)]` button.
         - **Explicit Minimap Controls**: Added dedicated `#minimap-controls-bar` with clickable Size (`[` / `]`) and Zoom (`-` / `+`) buttons with hotkey badges.
         - **Camera Head Tilt Controls**: Added `#camera-tilt-bar` with clickable `▲ Look Up (PgUp)`, `● Level (Home)`, and `▼ Look Down (PgDn)`.
         - **Complete Action Bar**: Extended action bar with all essential Angband commands (`⚔ Attack Space`, `🏹 Shoot f`, `✨ Cast m`, `🧪 Potion q`, `📜 Scroll r`, `🚪 Door o`, `💎 Get g`, `⏳ Rest R`, `🎒 Pack i`, `🛡 Gear e`, `⬇ Descend > / ⬆ Ascend <`, `📜 Classic Tab`, `❓ Help ?`).
         - **Dynamic Staircase Action Button**: Illuminates and pulses bright gold with directional label (`⬇ Enter Dungeon >`, `⬇ Descend >`, `⬆ Return to Town <`) when standing on stair tiles.
         - **Virtual Touch Diagonals**: Added 4 diagonal buttons (`NW 7`, `NE 9`, `SW 1`, `SE 3`) to the virtual touch D-pad.
         - **Controls & Commands Guide Sheet**: Integrated 6-tab modal guide opened via `⌨ Controls (?)` top button, `❓ Help (?)` action button, or physical <kbd>?</kbd> key.
         - **Top Landscape Opaque Message Feed Window**: Replaced ephemeral floating chips with an opaque landscape HUD window (#message-feed-window) at top center (`#080b12`, gold borders) that streams the complete narrative and combat feed, supports scrollback history, and includes `▲ Top`, `▼ Latest`, and `✕ Clear` controls.
         - **Resilient Death & Permadeath Transition**: Fixed unhandled exception in `hud.update` that previously blocked the death screen, ensuring the atmospheric death modal and embedded tombstone canvas (#death-terminal-canvas) appear immediately on lethal damage (`player.dead || player.hp <= 0`) while forwarding keys to dismiss prompts or restart.
         - **Warm Acoustic Footstep Audio & Movement Throttle**: Softened synthetic footstep frequencies (warm low-frequency thuds, lowpass surface friction, eliminated harsh 1.6kHz resonant pings) and reduced volume to subtle foley (`0.30`). Throttled footstep triggers per movement step to eliminate rapid machine-gun audio spikes.
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

### Priority 7: Menu Parity, Splash Key Requirement, Load Game Modal, Pause Menu, & Store Contextual Toolbar (COMPLETED)
- **Scope**: `server/public/index.html`, `server/public/css/dungeon.css`, `server/public/js/app.js`, `server/public/js/input.js`, `client/scripts/Main.cs`
- **Accomplishments**:
  - **Splash Screen Keypress Requirement**: Any keypress (Space, Enter, letters, numbers, arrow keys) or mouse click is required to advance from the splash screen to the main menu. Added shortcuts for Guide (`[2]`/`[G]`), Credits (`[3]`/`[C]`), and Wiki (`[W]`). Guaranteed that the splash screen never auto-advances.
  - **Main Menu 1:1 Parity**: Main menu provides all options: `[1] Continue Last Played`, `[2] Load Saved Game...`, `[3] Start from Scratch (Random Hero)`, `[4] Start from Scratch (Custom Hero)`, `[5] Game Guide & Primer`, `[6] Summary & Credits`, `[7] Angband Online Wiki & Manual`.
  - **Dedicated Load Saved Game Modal (`#load-modal`)**: Built ornate modal with dynamic queries to `/api/saves`. Displays all saved adventurers with name, class/race/level/depth summary, date, file size, and interactive `Load [Enter]` and `Delete [Del/D]` actions with confirmation safeguards.
  - **In-Game Pause Menu (`#pause-modal`)**: Pressing <kbd>Esc</kbd> in free-roaming 3D world (or clicking ⚙ Menu) opens the in-game Game Menu (matching Godot `Main.cs:1822`), offering Resume Game, Save Game Now (Ctrl-S), Load Other Character..., Start Over (Random/Custom), Guide, Fullscreen (F11), and Save & Quit to Main Menu.
  - **Contextual Store & Terminal Toolbar**: When entering stores (e.g. Armoury, General Store, Weaponsmith), the terminal card dynamically updates its title (e.g. `⚔ ARMOURY`) and buttons (`Exit Store (Esc)`), strictly hiding the `⚔ Quick Start Hero (@)` birth button during active play so players never confuse a shop screen with character creation.
- **Regression Repairs & Comprehensive UX/3D Polish Milestone**:
  - **Global Sound Mute**: Universal mute toggling across all game states, menus, and views via <kbd>Ctrl+M</kbd> shortcut, pause menu toggle in Godot, and `#btn-sound-toggle` in Web UI (`AudioManager.cs`, `Main.cs`, `input.js`, `audio.js`).
  - **Shop Menus & Action Bar**: Auto-dismissing rumor `-more-` prompts when entering stores, providing explicit Buy (`p`), Sell (`s`), Examine (`i`), and Exit (`Esc`) action buttons, and enabling direct row clicking to purchase store inventory items (`terminal.js`, `hud.js`, `index.html`, `app.js`).
  - **Hero Creation Guarding**: `btn-quick-birth`, `btn-term-reroll`, and `btn-term-custom` buttons strictly hidden once a player exists (`hasPlayer`), preventing birth options from intruding on shop or in-game terminal menus.
  - **Message Log in Town**: Message feed window is available and active from game start in town (`hasPlayer || frame.phase === 'play'`).
  - **Message Log & Minimap Separation + Automated `-more-`**: Message log relocated to avoid minimap overlap; removed distracting pulsing animation; automatically clears `-more-` prompts by sending `space` to engine so space is never required during message log reading.
  - **Mouse Resizable & Moveable HUD Windows**: Both the Minimap and Message Log windows are freely draggable via their header bars and resizable via bottom-right drag handles, with real-time canvas resizing and persistent layout coordinates in `localStorage`.
  - **Dungeon Undiscovered Terrain**: Reverted unmapped tiles (`FEAT_NONE`) to authentic dark subterranean void space rather than synthesizing fake granite walls.
  - **Item Models & Textures**: Resolved pebbles, stones, rocks, shots, and bullets to `Mineral` models/fbx and procedural stone geometries instead of arrows; routed darts to `Dart` models.
  - **Monster Models**: Fixed non-humanoid FBX models (`Rat`, `Snake`, `Spider`, `Frog`, `Wasp`) by removing improper `QueueFree()` calls; added model bindings for kobolds (`Puglin.glb`) and demons/imps (`Imp.glb`).
  - **Menu Lifecycle Audit & Store Isolation**:
    - Strictly quarantined character creation options (`Quick Start`, `Reroll`, `Custom`, `Advance`) away from store menus.
    - Inside shops (`inPlay && isStore`), the toolbar presents strictly shop options: `[ 💰 Buy (p) ] [ 🏷 Sell (s) ] [ 🔍 Examine (i) ] [ 🚪 Exit (Esc) ]`.
    - Fixed store entrance detection bug by parsing 2-hex feature indices (`parseInt(f.substring(px*2, px*2+2), 16)`), enabling instant automatic store menu display upon entering shop doors in town (feats 7..14).
    - Unblocked store `-more-` auto-advance in `onFrame` so initial store rumors/messages never require manual spacebar presses.
    - On character creation/review (`phase === 'setup'`), `#store-actions-bar` is strictly hidden (`display: none`).
  - **Message Log Clean Start**:
    - Quarantined all message capture (`frame.messages` and `frame.term.rows[0]`) strictly to active gameplay (`inPlay`).
    - On transition into active play (`inPlay && !wasInPlay`), message feed starts exclusively with the welcoming greeting:
      `Welcome to the Town of Angband! Visit the General Store and Armory to equip your journey.`
    - Baseline-seeds engine ring-buffer (`prevMessages`) and `lastTermRow0` on play entry, preventing Angband's creation/history messages (`Accept character history? [y/n]`, `' . .`) from polluting the message log.
    - Added keyword filter rejecting any creation or history prompt fragments.
  - **Confirmed Default Window Coordinates & Scale**:
    - Minimap Radar: `top: 14px; left: 18px;`
    - Message Log: `top: 14px; left: 320px;`
- **Character Creation Navigation & Main Menu Escape**:
  - `Escape` keypress and `Back (Esc)` toolbar button now detect character creation beginning (`!inPlay && !isReviewScreen`) and return directly to the Main Menu overlay (`returnToMainMenu()`), cleanly disconnecting and preventing the blank screen lock.
  - Review screen `Back (Esc)` continues to step back to character creation beginning (`'s'`).
- **Message Log in Town Display & Width Constraint Fix**:
  - Corrected `#message-feed-window` CSS constraints to `width: min(580px, calc(100vw - 340px)); min-width: 260px; height: 180px; min-height: 90px;`, fixing layout collapse on standard viewports.
  - Added safe clamping in `makeWindowDraggableAndResizable` so no stored `localStorage` values can position the window offscreen or collapse it.
  - Ensured `this.messageFeedWindow` is lazily queried and forced visible with `display: flex; visibility: visible; opacity: 1` whenever `inPlay` (`frame.phase === 'play'` or `frame.player.name`).
  - Guaranteed town welcome message seeding on town entry.
  - Removed duplicate `network.sendKey('space')` from `hud.js` to eliminate racing double-space key events.
- **Automatic Shop Menu Opening & Action Bar Isolation**:
  - Updated `needsTerminal(frame)` to immediately route to the terminal when `frame.ui.overlay > 0` or whenever stepping onto a store entrance tile (feats 7..14).
  - Expanded `updateTerminalToolbar(frame)` to detect all Angband store variations (`Armoury`, `Alchemy Shop`, `Magic User's`, `Weapon Smiths`, `Your Home`, `Store Inventory`, `Home Inventory`) and mapped store names from `playerFeat`.
  - Exclusively displays `store-actions-bar` (`💰 Buy (p)`, `🏷 Sell (s)`, `🔍 Examine (i)`, `🚪 Exit (Esc)`) and hides generic advance/creation buttons.
  - Guarded auto-space flushing so spaces are NEVER auto-sent while inside a store overlay (`!isOverlay`), preventing store interactions from being unintentionally cancelled or dismissed.
- **Verification**: 11/11 engine bridge smoke tests, 11/11 node server unit tests, 0 warnings dotnet build across client/angband3d.csproj. Live deployment verified on Google Cloud Run:
  - `angband3d-cloud` (us-central1): Revision `angband3d-cloud-00047-6rl` (https://angband3d-cloud-564958309282.us-central1.run.app)
  - `angband3d-cloud` (us-east1): Revision `angband3d-cloud-00008-2gt` (https://angband3d-cloud-564958309282.us-east1.run.app)
  - `angband3d-web` (us-central1): Revision `angband3d-web-00005-djt` (https://angband3d-web-564958309282.us-central1.run.app)
  - Live WebSocket handshake, health check, save API, store entry, and gameplay action verified via automated cloud integration test suites.

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
