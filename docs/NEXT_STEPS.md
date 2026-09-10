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

## Roadmap & Next Steps for Next Session

### Priority 1: First-Person Hand & Rig Mesh Upgrades
- **Skeletal / Rigged Hand Meshes or Pre-Made CC0 First-Person Rigs**:
  - Replace procedural multi-mesh geometry with an imported rigged low-poly arm/hand asset (e.g., KayKit or Kenney first-person hand pack) with dedicated bone animations for idling, walking, swinging, blocking, shooting, and casting.
  - Add glove/gauntlet overlays based on equipped armor (`EQUIP_GLOVES` / `EQUIP_BODY_ARMOR`).

### Priority 2: Enhanced Equipment Visuals & Particles
- **Dynamic Particle Effects on Held Items**:
  - Emissive flame & smoke particles for torches.
  - Glow and rune trails for enchanted / ego weapons (`+to_hit`, `+to_dam`, branded weapons like fire/frost/lightning).
  - Arcane charge particle effects on wands and spellbooks during casting.

### Priority 3: Monster Animations & State Feedback
- **Monster Visual Behaviors**:
  - Integrate walk, attack, hurt, and death animations from KayKit character assets.
  - Sleeping / Asleep indicators (floating "Zzz" or rested poses).
  - Fear / Fleeing visual state (retracting or turning away).

### Priority 4: Advanced Minimap & HUD Refinements
- **Minimap Interactive Styling**:
  - Radar-style directional compass cone for player facing direction.
  - Fog of war reveal smoothing.
  - Customizable UI widget positioning.

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
