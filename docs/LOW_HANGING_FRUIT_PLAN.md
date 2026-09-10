# Low Hanging Fruit Enhancement Plan & Sequential Execution Queue

**Project**: Angband3D  
**Tracking Context**: Saved for sequential execution one step at a time across chat sessions.  
**Execution Command**: Prompting "begin the low hanging fruit" or "execute step N of low hanging fruit" starts the active step.

---

## 📋 Sequential Execution Roadmap

### 🟢 Step 1: Monster Equipment & Unarmed Weapon Resolver
* **Status**: Completed (Verified & compiled)
* **Scope**: Client (`client/scripts/MonsterModelResolver.cs`)
* **Objective**: Fix monster model weapon attachments so creatures accurately reflect what they are actually wielding or dropping.
* **Key Tasks**:
  1. **Unarmed / Non-combatants**: Townsfolk, beggars, drunkards, village idiots, and creatures without weapon blows (e.g. `DROOL`, `BEG`, `TOUCH`) have both left and right hand weapon slots hidden (bare hands).
  2. **Role-Specific Weapon Selection**:
     - *Archers / Scouts / Snipers*: Show Crossbow / Bow in hand slot.
     - *Warriors / Knights / Guards / Centurions*: Show Sword in right hand and Shield in left hand.
     - *Mages / Sorcerers / Shamans / Necromancers*: Show Wand / Staff in right hand, empty left hand or Spellbook.
     - *Barbarians / Berserkers / Ruffians*: Show Battleaxe / Greathammer (two-handed posture or 1H main weapon without shield).
     - *Rogues / Assassins / Thieves / Cutpurses*: Show Dagger in right hand, empty left hand.
  3. **Data-Driven Model Rule Extension**: Extend `MonsterModelRule` with an `EquipmentRole` enum or custom weapon config filter to cleanly map race names and glyphs without hardcoded mess.

---

### 🟢 Step 2: First-Person Viewmodel (Player Hands & Torch / Weapon)
* **Status**: Completed (Verified & compiled)
* **Scope**: Client (`client/scripts/ViewModel.cs`, `DungeonWorld.cs`)
* **Objective**: Add first-person rigged or mesh hands and held equipment in screen space.
* **Key Tasks**:
  1. Create `ViewModel.cs` parented to the main `Camera3D`.
  2. Equip active torch in left hand (`client/assets/models/dungeon/torch_lit.gltf.glb`) when `light > 0` or in dungeon; show shield if equipped.
  3. Equip active weapon in right hand (`sword_1handed.gltf`, `wand.gltf`, or bare fist based on equipped weapon in frame payload).
  4. Implement sinusoidal walk bobbing and smooth yaw/pitch inertia sway.
  5. Add attack thrust/swing action animation on melee attack actions.

---

### 🟢 Step 3: Floating Combat Numbers & Feedback VFX ("Juice")
* **Status**: Completed (Verified & compiled)
* **Scope**: Client (`client/scripts/DungeonWorld.cs`, `Main.cs`)
* **Objective**: Provide kinetic visual feedback on hits, misses, damage, and kills.
* **Key Tasks**:
  1. Parse combat messages and monster `hp` deltas in `OnFrame`.
  2. Spawn billboarding `Label3D` floating text above targets (e.g. `-14` in bright orange, `CRIT! 28` in gold, `MISS` in silver) that floats up and fades over 0.65s.
  3. Spawn `CpuParticles3D` directional hit sparks matching monster blood/element color.
  4. Spawn death dissolve poof on monster elimination.
  5. Apply trauma-based camera shake on player taking damage.

---

### 🟢 Step 4: Corridor Wall Torches & Procedural Dungeon Clutter
* **Status**: Completed (Verified & compiled)
* **Scope**: Client (`client/scripts/DungeonClutterResolver.cs`, `client/scripts/DungeonWorld.cs`)
* **Objective**: Populate empty dungeon corridors and rooms with decorative props and wall sconces.
* **Key Tasks**:
  1. Automatically mount `torch_mounted.gltf.glb` sconces with subtle point lights every 6–8 tiles along corridor walls.
  2. Place corner pillars (`pillar.gltf.glb` / `column.gltf.glb`) at concave room wall joints.
  3. Deterministically scatter crates, barrels, floor grates, and banners based on level seed in room corners and dead-ends.

---

### 🟢 Step 5: Positional Sound Effects & Audio Cues
* **Status**: Completed (Verified & compiled)
* **Scope**: Client (`client/scripts/AudioManager.cs`)
* **Objective**: Integrate immersive audio for movement, combat, doors, and dungeon exploration.
* **Key Tasks**:
  1. Footstep audio synchronized to movement cadence (stone vs outdoor terrain).
  2. Melee impact sound effects (blade swing, blunt hit, monster screech/grunt).
  3. Spell casting and missile projectile whoosh/impact sounds.
  4. Door open/close/creak and stairs descending sounds.

---

### 🟢 Step 6: Minimap Window Geometry Scaling vs Grid Zoom Radius & HUD Customization
* **Status**: Completed (Verified & compiled)
* **Scope**: Client (`client/scripts/Overlay.cs`, `Main.cs`)
* **Objective**: Decouple physical minimap viewport dimensions from ASCII tile zoom radius, adding dedicated resize keys and configurable HUD layout profiles.
* **Key Tasks**:
  1. Separate `MinimapScale` (viewport window size) from `MinimapZoom` (grid tile radius / font size).
  2. Map dedicated secondary keys (e.g. `Ctrl+PgUp/PgDn` or `[` / `]`) for window size scaling alongside `PgUp/PgDn` / `+`/`-` for map zoom.
  3. Add HUD opacity toggles, font scaling presets, and mini-map dock position anchors (top-right vs bottom-right).

---

### � Step 7: Character Height World Scaling & Dynamic Viewmodel Hands/Wieldables
* **Status**: Completed (Verified & compiled)
* **Scope**: Client (`client/scripts/ViewModel.cs`, `DungeonWorld.cs`, `ItemModelResolver.cs`)
* **Objective**: Scale world perception and first-person viewmodel hands dynamically based on character height / race, and equip the highest-fidelity 3D models representing all wielded weapons, shields, lights, and items.
* **Key Tasks**:
  1. **Visual World Scaling & Eye Height**: Scale camera eye height ($0.70\text{m} - 2.10\text{m}$), field of view tilt perception, step bobbing cadence, and door/monster relative scale based on player race (Hobbit/Gnome ~36-42", Dwarf ~48-54", Human/Elf ~66-74", Half-Troll/Ogre ~88-102") and exact height stat `ht`.
  2. **Viewmodel Hands & Scaling**: Render stylized/mesh hands and forearm sleeves holding the items, with hand geometry scale and reach position proportional to character height ($0.60\times$ for Hobbits to $1.40\times$ for Half-Trolls) with customizable skin/cuff tones.
  3. **Comprehensive Equipment Model Resolver**: Map Angband's complete wieldable catalogue (weapons, polearms, bows/slings, wands, staves, books, shields, lanterns/torches, potions/horns) to the best matching CC0 3D models with precise grip offsets.

---

## 🚀 Execution Instructions for New Chat Sessions

When starting the next session, prompt:
> **"Implement Tier 4 Depth Biomes and Atmospheric Lighting"** (or **"Continue the Tier 4 visual overhaul"**)

The assistant will read `docs/NEXT_STEPS.md`, `docs/GRAPHICS_HANDOVER.md`, and `/memories/repo/graphics_handover.md`, implement **Priority 1: Depth-Based Biomes & Atmospheric Lighting (6 depth zones, volumetric fog, color grading, and PBR textures)** in `DungeonWorld.cs`, verify the build with `dotnet build client/angband3d.csproj`, run smoke tests, and capture screenshots to verify the enhanced aesthetics.
