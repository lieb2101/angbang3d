# Architecture

## Why a front end and not a rewrite

Angband has roughly thirty years of content and balance in it: hundreds of
monsters, a thousand item kinds, artifacts, curses, ego items, level feelings,
vaults. Reimplementing those rules faithfully is a multi-year project, and the
result would be a worse Angband.

Angband already separates game logic from display, ships a documented skeleton
front end (`src/main-xxx.c`), and has a headless one (`src/main-test.c`). Adding
a front end is a supported extension point. So we add one that speaks JSON, and
spend all our effort on the 3D client instead.

Alternatives considered and rejected:

- **Reimplement the rules in C#.** Years of work to reach parity.
- **Parse Angband's data files, write our own rules engine.** Still years; the
  data files describe content, not behaviour.
- **Screen-scrape the ASCII output.** Loses all semantics; no way to know what a
  glyph *is*.

## Repository layout

```
engine/          Angband fork, branch `bridge`, based on tag 4.2.6
  src/main-bridge.c    the front end
  src/bridge-json.c    JSON writer
  src/bridge-json.h
  src/cmake/macros/BRIDGE_Frontend.cmake
client/          Godot 4 client
tools/           build scripts, reference client, tests
docs/            protocol and architecture
```

`engine/` is a fork with its own history and an `upstream` remote pointing at
angband/angband. It is deliberately not committed to the parent repository; once
the fork is published it becomes a submodule.

## Keeping the fork rebasable

This is the single most important constraint in the project. A fork that cannot
take upstream updates is a dead end, and Angband is actively maintained.

All bridge code lives in **new files**. Exactly four existing files are touched,
and only to register the front end the same way every other front end is
registered:

| file | change |
|---|---|
| `src/main.h` | declare `init_bridge` and `help_bridge` |
| `src/main.c` | one entry in the `modules[]` table |
| `CMakeLists.txt` | mirror the four `SUPPORT_TEST_FRONTEND` blocks |
| `src/Makefile.src` | `BRIDGEMAINFILES`, added to `ALLMAINFILES` |

None of these touch game logic, so upstream changes almost never conflict.

Where the bridge needs behaviour that `main.c` does not provide, it does the
work itself rather than patching `main.c`. For example `create_needed_dirs()` is
called from `init_bridge()` because upstream only calls it under `#ifdef UNIX`
and the Windows front end bypasses `main.c` entirely — without this, saving
fails on Windows for every `main.c`-based front end.

## Keypresses, not commands

Angband has a command queue (`cmdq_push`) that looks like the natural injection
point. It is not, for our purposes.

Enormous parts of Angband's interface are prompts driven from inside the game
logic: which item, which direction, which spell, store haggling, character
birth, wizard confirmations. Those are not expressible as queued commands. A
front end that pushed commands would have to reimplement every one of them
before the game was even playable.

Feeding `Term_keypress()` instead means the entire existing interface works
immediately and for free. The cost is that prompts arrive as text on the raw
terminal channel, which the client overlays until native panels replace them —
a deliberate, reversible trade.

## The two channels

Angband's display is a character grid, but its *state* is structured and
reachable. The bridge exposes both:

- The **structured channel** is built from `map_info()` and
  `grid_data_as_text()`, the same functions Angband's own renderer calls. That
  guarantees the client sees exactly what the game intends to show, including
  detection, hallucination and light effects, with no separate visibility logic
  to keep in sync.
- The **raw terminal channel** carries everything that only exists as text.

Over time the raw channel shrinks as native UI replaces each panel. It never has
to disappear entirely for the game to be playable.

## Frame timing

A frame is emitted only when Angband genuinely blocks for input
(`TERM_XTRA_EVENT` with the wait flag set). Non-blocking input polls return "no
event" immediately. This means one frame per decision point rather than one per
redraw, which keeps full-level serialisation affordable.

Serialising a level is a `map_info()` call per grid, roughly 12k calls on a large
level. That is fine at turn-based rates. Clients that only need the text UI can
turn it off with `map off`.

## Robustness rules

Frames can be emitted at awkward moments — during level generation, before the
player has been placed, before a character exists. Any grid-relative lookup must
therefore be guarded; `bridge_in_play()` exists for this. An unguarded read here
crashes the process and appears to the client as truncated JSON.

## System Architecture & Data Flow

```mermaid
flowchart TD
    subgraph Engine [Angband C Engine Process]
        A[Angband Game Core] -->|blocks for input| B[src/main-bridge.c]
        B -->|serializes state| C[src/bridge-json.c]
    end

    subgraph IPC [Standard Streams IPC]
        C -->|stdout JSON stream| D[BridgeClient.cs]
        D -->|stdin key commands| B
    end

    subgraph Client [Godot 4 C# Frontend]
        D -->|Parsed Frame Object| E[Main.cs: View & State Coordinator]
        E -->|ViewMode: World| F[DungeonWorld.cs: 3D Renderer]
        E -->|ViewMode: Terminal / Map / HUD| G[Overlay.cs: 2D Canvas]
        F --> H[MultiMesh Static Batches]
        F --> I[Monster Entities & 3D Models]
        F --> J[3D Item Pickups]
        E -->|Raw Key Events| K[Input Dispatcher]
        K -->|Camera Turn 0-turn| F
        K -->|Relative Numpad / Arrow Key| D
    end
```

## View Routing State Machine (`NeedsTerminal`)

Angband switches between different interaction contexts (dungeon exploration, character birth, inventory, store shopping, and direction/targeting prompts). The client routes these seamlessly:

| Condition in Frame | View Mode | Visual Presentation |
|---|---|---|
| `phase != "play"` | `ViewMode.Terminal` | Character creation / splash / death screens |
| `ui.overlay > 0` | `ViewMode.Terminal` | Menus, inventory, equipment, stores |
| `!ui.awaiting_command && !ui.more` | `ViewMode.Terminal` | Direction prompts, targeting, yes/no queries |
| `ui.more == true` | `ViewMode.World` | `-more-` message pauses stay in 3D with HUD prompt |
| `_mapMode == true` | `ViewMode.Map` | Full-level 2D tactical map |
| Normal dungeon play | `ViewMode.World` | First-person 3D world with HUD overlays |

## 3D Rendering & MultiMesh Batching Pipeline

To render a 12,000-tile Angband dungeon floor at 144+ FPS on modern discrete and integrated GPUs:
1. **MultiMesh Instance Batches**: Walls, floors, ceilings, stairs, and rubble are grouped into contiguous `MultiMeshInstance3D` nodes by material bucket (`Kind` enum). All static geometry in the scene renders in ~8 draw calls.
2. **Sightline Culling & Overdraw Elimination**: In dungeons (depth > 0), only geometry within direct Line of Sight (`in_view` flag) is uploaded to the instance buffer. Solid rock beyond walls remains empty void, preventing see-through wall glitches and eliminating hidden-surface rendering overhead.
3. **Procedural PBR Textures & Normal Maps**: Multi-octave ashlar limestone masonry, chisel bevels, flagstone floors, cavern ceilings, incandescent magma fissures, and crystalline quartz seams are generated procedurally on startup with normal maps and calibrated dielectric roughness.
4. **Orientation-Aware Portals**: Door frames detect surrounding wall geometries and orient themselves across North-South or East-West corridor passages with accurate open ($70^\circ$ ajar), closed, and broken states. Shop doors in town orient outwards into streets and plazas with heraldic gilded number plaques.
5. **Dynamic Entity Tracking**: Visible monsters and items are tracked across frames with persistent `MonsterEntity` instances, interpolating position and orientation smoothly while blending walk and idle animations.

## Dynamic Character Height & World Scaling Architecture

A standout feature of Angband3D is the **Dynamic Character Height & Perspective Scaling Subsystem**:
- **Life-Scale Height Mapping**: Angband rolls character physical height in inches (`ht` property) based on race (Halflings ~36-44", Dwarves ~48-56", Humans/Elves ~68-76", Half-Trolls ~84-104").
- **Dynamic Eye Height Elevation**: Camera elevation scales continuously:
  $$\text{CurrentEyeHeight} = \text{Clamp}\left(\frac{\text{ht}}{72.0} \times 1.62\text{m}, 0.82\text{m}, 2.45\text{m}\right)$$
  - Short characters (~0.88m–1.05m) experience high ceilings, towering stone doorways, and intimidating monsters.
  - Tall characters (~1.95m–2.35m) experience sweeping views peering down upon subterranean corridors.
- **Perspective FOV Adjustment**: Field of view adjusts dynamically between $86^\circ$ (short races, wider situational awareness) and $82^\circ$ (tall races, focused perspective) without fish-eye distortion.
- **Viewmodel Hand & Reach Scaling**: Forearm reach, hand size, and weapon dimensions dynamically scale with `CurrentHeightRatio`.
- **Acoustic Footstep Pitch Modulation**: Footstep playback frequency modulates with character mass ($1.0 / \text{CurrentHeightRatio}^{0.40}$), producing brisk light steps for halflings and deep thuds for trolls.

## First-Person Viewmodel & Hand Rig Architecture

The viewmodel system (`ViewModel.cs`) renders the player's hands, sleeves, and wielded equipment in screen space parented to `Camera3D`:
1. **Procedural Arm Rig**: Low-poly forearm sleeves with class-specific cloth materials, metallic wrist bracers/cuffs, palms, opposable thumbs, and 4 sculpted fingers wrapping held handles.
2. **Dynamic Racial Tinting**: Modulates hand albedo with race-specific skin tones (fair elven, golden human, ruddy dwarven, obsidian dark-elf, mossy troll).
3. **Equipped Item Matching**: Resolves wielded weapons (swords, daggers, 2H battleaxes, polearms, bows, wands, staves, spellbooks, shields, burning torches, or bare fists) directly from player equipment telemetry.
4. **Kinematic Motion**:
   - Sinusoidal walk bobbing synchronized to movement speed.
   - Inertia yaw/pitch sway damping camera turns.
   - Attack slash/thrust animations on melee strikes.
   - Radiant casting surges on spell recitation.
   - Defensive recoils and camera screen shake on taking damage.

## Zero-Allocation Procedural Audio Architecture

The audio engine (`AudioManager.cs`) generates rich 16-bit 44.1kHz PCM sound effects synthesized on startup:
1. **Zero External Audio Dependencies**: Eliminates large audio asset downloads and disk streaming latency.
2. **Pre-Allocated Sound Pools**: 8 2D `AudioStreamPlayer` channels and 12 3D spatial `AudioStreamPlayer3D` channels with inverse-square distance attenuation.
3. **Round-Robin Zero-Allocation Dispatch**: Audio playback reuses active players without instantiating objects or generating runtime garbage collection pressure.

## Dual-Mode Minimap & Window Geometry Scaling

The UI subsystem (`Overlay.cs`) decouples physical HUD window sizing from ASCII grid tile zoom:
1. **Physical Window Sizing**: Scaled via `[` / `]` or `Ctrl+PgUp/PgDn` from compact 140x100px up to widescreen tactical monitors.
2. **Grid Tile Zoom Radius**: Scaled via `+` / `-` or `PgUp/PgDn` from tight 10-tile radius up to 50-tile wide-angle radar.
3. **Tactical 2D Overlay**: `Shift-M` toggles full-level tactical view with player orientation cone and fog-of-memory rendering.


## Licensing and content

The project is GPL v2, inherited from Angband. That permits commercial use and
donations.

Angband's *content*, however, is heavily Tolkien-derived — Morgoth, Sauron, the
artifacts, much of the bestiary. Angband is untroubled as a free non-commercial
project. Distributing this in a form that accepts money is a different risk.
profile.

The mitigation is cheap and lives in the data files: `engine/lib/gamedata/`
contains monsters, artifacts and objects as editable text. Renaming the
Tolkien-specific content is a content edit, not a code change, and other Angband
variants have precedent for it. Do that before any release that accepts money.

Art and audio assets carry their own licences, tracked in
`client/assets/CREDITS.md`. Only CC0 or equivalently permissive sources are used.
