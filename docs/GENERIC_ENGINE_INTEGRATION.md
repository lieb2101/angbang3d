# Generic Game Engine Integration Guide
*How to Port the Angband3D First-Person 3D Architecture to Any Roguelike or Grid-Based Engine*

## 1. Architectural Overview & Philosophy

The core architectural breakthrough of `angband3d` is the **Authoritative Engine / Headless Bridge / Native 3D Client** pattern:

```mermaid
flowchart TD
    subgraph Engine [Authoritative Game Engine (C / C++ / Rust / Zig)]
        A[Game Core Logic & Content] -->|Blocks for Input| B[Engine Bridge / Windowproc Hook]
        B -->|Serializes Full State| C[JSON Stream Writer]
        B <--|Keypress Injection| D[Stdin / Socket Command Reader]
    end

    subgraph IPC [Transport Layer]
        C -->|stdout or TCP socket| E[IGameEngineBridge]
        E -->|stdin or TCP socket| D
    end

    subgraph Client [Godot 4 C# 3D Presentation Client]
        E -->|Parsed JSON Frame| F[Main: State & View Coordinator]
        F -->|ViewMode: World| G[DungeonWorld: 3D MultiMesh Renderer]
        F -->|ViewMode: Terminal / Map| H[Overlay: 2D Canvas & HUD]
        F -->|Combat / Action Events| I[AudioManager & VFX Spawners]
        G --> J[Monster / Item Model Resolvers]
        G --> K[First-Person Camera & ViewModel]
    end
```

### Why This Pattern Wins Over Alternatives
1. **Zero Rules Drift**: Decades of gameplay balance, AI behavior, level generation, item drop curves, and edge-case mechanics remain 100% authoritative in the original source engine.
2. **Rebasability & Maintainability**: The bridge touches almost zero upstream game code, allowing seamless upstream merges.
3. **Transport Agnostic**: The client connects via `IGameEngineBridge`. The engine can run as a local child process, inside a Docker container, in a WebAssembly worker, or on a remote multiplayer game server over TCP/WebSockets.
4. **Instant 0-Turn Ergonomics**: First-person camera rotation, free looking, UI menus, minimap zooming, and viewmodel inertia sway are handled entirely in the client at 144+ FPS without consuming game engine turns.

---

## 2. The Minimal C/C++ Engine Bridge Skeleton (~200 Lines)

To integrate any source engine, you only need to provide 4 core hooks:

### A. Input Interceptor Hook
Whenever the engine's main loop blocks to wait for a player keypress, emit a frame and wait for client input:

```c
/* Pseudocode / Minimal Hook */
int engine_get_key_hook(void) {
    emit_frame(stdout);  /* Send JSON state */
    
    char buf[1024];
    while (fgets(buf, sizeof(buf), stdin)) {
        if (strncmp(buf, "key ", 4) == 0) {
            int keycode = translate_virtual_key(buf + 4);
            return keycode; /* Inject directly into engine event queue */
        }
        if (strncmp(buf, "quit", 4) == 0) {
            exit(0);
        }
    }
    return 0;
}
```

### B. Frame Serializer
Serialize the standard Bridge Protocol v1 object:

```json
{
  "t": "frame",
  "seq": 42,
  "phase": "play",
  "ui": { "overlay": 0, "awaiting_command": true, "more": false },
  "player": {
    "name": "Player", "race": "Human", "class": "Warrior",
    "x": 14, "y": 8, "depth": 1, "hp": 30, "hp_max": 30,
    "sp": 0, "sp_max": 0, "light": 2, "gold": 150
  },
  "map": {
    "w": 80, "h": 24,
    "rows": [
      { "g": "...", "a": "...", "f": "...", "l": "..." }
    ]
  },
  "monsters": [
    { "id": 1, "x": 16, "y": 8, "glyph": "r", "attr": 3, "race": "Cave rat", "hp": 4, "hp_max": 4 }
  ],
  "objects": [
    { "x": 15, "y": 9, "glyph": "!", "attr": 2, "name": "Potion of Cure Light Wounds" }
  ],
  "messages": [
    { "text": "You enter depth 1.", "count": 1, "attr": 1 }
  ],
  "term": {
    "w": 80, "h": 24, "cx": 14, "cy": 8, "cursor": false,
    "rows": [{ "g": "...", "a": "..." }]
  }
}
```

---

## 3. Adapting Popular Classic Engines

### NetHack
- **Where to Hook**: `win/tty/` or implement a custom `windowprocs` structure in `win/bridge/`.
- **Key Functions**:
  - `bridge_init_nhwindows()`, `bridge_player_selection()`
  - `bridge_display_nhwindow(WIN_MAP)` -> trigger `emit_map()`
  - `bridge_nhgetch()` -> `bridge_pump()` awaiting `key <spec>`
- **Terrain Mapping**: Map NetHack's `levl[x][y].typ` (e.g. `STONE`, `ROOM`, `CORR`, `DOOR`, `STAIRS`) directly to `f` feature indices.

### Dungeon Crawl Stone Soup (DCSS)
- **Where to Hook**: `crawl-ref/source/ui.cc` or custom `cio` wrapper.
- **Key Data Structures**:
  - `env.grid(x, y)` -> dungeon feature type (`DNGN_FLOOR`, `DNGN_ROCK_WALL`, `DNGN_CLOSED_DOOR`, etc.)
  - `env.mons[x]` -> monster entity list with full HP, stance, and enchantment statuses.
- **Input Stream**: Inject into `cio.cc::get_ch()`.

### Moria / Umoria / Sil / OAngband / ZAngband
- **Where to Hook**: Almost identical to Angband 4.2.6!
- Copy `engine/src/main-bridge.c` and `engine/src/bridge-json.c`. Register the frontend in `main.c` via the `modules[]` table.

### Brogue
- **Where to Hook**: Replace `tcod-platform.c` or `curses-platform.c` with `bridge-platform.c`.
- Brogue maintains direct arrays `grid[x][y]` and `monsters` in `Brogue.h`, making `emit_map()` and `emit_monsters()` trivial (<100 lines of C).

---

## 4. Client-Side Abstraction & Extension Guide

### Step 1: Implement `IGameEngineBridge`
If connecting via standard I/O child process, reuse `BridgeClient.cs`. If connecting via TCP or WebSocket, create a socket adapter implementing `IGameEngineBridge`:

```csharp
public class TcpBridgeClient : Node, IGameEngineBridge
{
    public JsonElement? Frame { get; private set; }
    public JsonElement? Hello { get; private set; }
    public IReadOnlyDictionary<int, (string Name, bool Passable)> Features => _features;
    public bool Connected => _tcpStream != null;
    public bool Busy { get; private set; }
    public long Seq { get; private set; }

    public void SendKey(string spec) => Send($"key {spec}");
    public void Send(string command) { /* write string to TCP socket */ }
    public void Stop() { /* close socket */ }
}
```

### Step 2: Configure Visual Terrain Mappings (`DungeonWorld.cs`)
Map your engine's terrain enumeration in `DungeonWorld.KindOf()`:

```csharp
private static Kind KindOf(int feat) => feat switch
{
    0 => Kind.Skip,
    1 => Kind.Floor,
    2 => Kind.DoorClosed,
    3 => Kind.DoorOpen,
    4 => Kind.StairsDown,
    5 => Kind.StairsUp,
    6 => Kind.Wall,
    7 => Kind.Lava,
    _ => Kind.Wall
};
```

### Step 3: Register 3D Character & Item Models
Use the declarative registration APIs in `MonsterModelResolver` and `ItemModelResolver`:

```csharp
// Register custom 3D glTF model for Orcs ('o')
MonsterModelResolver.RegisterModelRule('o', new MonsterModelRule(
    modelPath: "res://assets/models/monsters/orc.gltf",
    scale: 1.1f,
    equipment: EquipmentRole.Warrior
));

// Register 3D model for Potion pickups ('!')
ItemModelResolver.RegisterItemModel('!', "res://assets/models/items/potion_red.glb", 0.5f);
```

---

## 5. Summary Checklist for New Engine Ports

| Phase | Milestone | Expected Effort |
|---|---|---|
| **Engine (C/C++)** | Add JSON writer (`bridge-json.c`) & input interceptor (`main-bridge.c`) | 2 - 4 hours |
| **Engine (Build)** | Add `-mbridge` flag or compilation macro to build system | 30 minutes |
| **Protocol** | Verify `{"t":"hello"}` and `{"t":"frame"}` output via Python script | 1 hour |
| **Client** | Connect `BridgeClient` to engine executable | 15 minutes |
| **Client** | Configure `KindOf()` terrain index mapping | 30 minutes |
| **Juice** | Configure custom monster models and audio triggers | 1 - 2 hours |
