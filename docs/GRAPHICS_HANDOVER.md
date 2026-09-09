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
- **Spectral Undead & Transparency**:
  - Spectral material pass for incorporeal creatures (ghosts, spectres, wraiths, shadows, phantoms) with translucent alpha blend and spectral emissive glow.
- **Humanoid & Archetype Model Distribution**:
  - Skeletons, Liches, Vampires, and Zombies map to distinct class gear: `Skeleton_Warrior`, `Skeleton_Mage`, `Skeleton_Rogue`, `Skeleton_Minion`.
  - Townspeople (`t`), adventurers, warriors, paladins, rogues, rangers, mages, clerics, barbarians, and mercenaries mapped with scale adjustments (e.g. Halflings/Dwarves $0.65\times$ vs Elves/Humans $0.98\text{--}1.05\times$).
  - Giants, Balrogs, and Trolls scaled with imposing proportions ($1.3\text{--}1.75\times$).
- **Specialized 3D Procedural Creature Tokens**:
  - **Floating Eye (`e`)**: 3D sclera sphere with illuminated colored iris and dark pupil.
  - **Slimes, Jellies & Worms (`j`, `i`, `w`)**: Translucent gelatinous dome with glowing pulsating inner nucleus.
  - **Molds & Mushrooms (`m`, `,`)**: Procedural mushroom clusters with glowing spore caps.
  - **Arachnids, Centipedes, Insects (`S`, `K`, `a`, `c`, `I`)**: Multi-segment body shell with glowing multi-eyes.
  - **Canines & Beasts (`C`, `f`, `q`, `r`, `Z`)**: Four-legged beast geometry with snout and glowing eye slots.
  - **Dragons & Hydras (`d`, `D`, `M`)**: Draconic spire with curved horn crests and blazing eyes.
  - **Elementals & Vortices (`E`, `v`)**: Faceted crystal prism with spinning orbital rings.
  - **Golems (`g`, `X`)**: Monolithic stone column with glowing runic visor slit.
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
  - **Closed**: Sturdy oak plank door leaf with forged iron straps, round rivets, and latch ring.
  - **Open**: Swung-open door leaf resting against the corridor wall jamb.
  - **Broken**: Splintered planks scattered across the floor.
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

## 4. Next Priorities for Future Work
1. **Modular Town Buildings**: Place individual 3D roof/chimney props or modular building models in town cells.
2. **Combat Animations**: Trigger attack / hit reaction animations during player and monster turns.
3. **Sound Effects**: Add 3D spatial audio cues for footfalls, door opening/bashing, and combat.

