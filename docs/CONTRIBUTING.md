# Contributing

## Ground rules

1. **Never break the fork's rebasability.** New behaviour in the engine goes in
   new files. If you think you need to edit an existing Angband source file,
   look for a way to do it from `main-bridge.c` first. See
   [ARCHITECTURE.md](ARCHITECTURE.md).
2. **Never change game rules.** A save produced through the bridge must remain a
   valid Angband save. Balance, content and mechanics belong upstream or in
   `engine/lib/gamedata/`.
3. **Protocol changes are contract changes.** Adding fields is fine. Removing or
   repurposing them needs a version bump and a note in
   [PROTOCOL.md](PROTOCOL.md).

## Setting up

```powershell
./tools/bootstrap.ps1   # MSYS2 + MinGW toolchain (Windows, one time)
./tools/build.ps1
python tools/smoke_test.py
```

On Linux or macOS you need GCC, CMake, Ninja and ncurses, then:

```sh
cd engine
cmake -G Ninja -DSUPPORT_BRIDGE_FRONTEND=ON -DSUPPORT_GCU_FRONTEND=ON -B build
cmake --build build
```

At least one graphical front end must be enabled. With none selected, CMake
picks the Windows front end, which requires libpng and disables the bridge.

## Testing

- `python tools/smoke_test.py` — bridge acceptance tests. Must pass.
- `dotnet build client/angband3d.csproj` — Godot C# client build. Must compile with 0 errors.
- `cmake --build engine/build -t allunittests` — Angband's own 932 unit tests.
  Must stay at 100%.

Both run in CI. A change that breaks upstream tests is a change to game
behaviour, which rule 2 forbids.

## How to Contribute Features

### 1. Adding New 3D Monster Models
Monster models are mapped declaratively in `client/scripts/MonsterModelResolver.cs`.
To map a new 3D model (e.g., GLTF/GLB in `client/assets/models/characters/`):

```csharp
// In MonsterModelResolver.InitModelRules() or at runtime via RegisterModelRule:
RegisterModelRule('k', "res://assets/models/characters/Kobold.glb", scale: 0.75f, speed: 1.1f);

// With keyword filtering:
RegisterModelRule('s', "res://assets/models/characters/Skeleton_King.glb", scale: 1.3f,
    matcher: name => name.Contains("king") || name.Contains("lord"));
```

### 2. Adding New 3D Item Pickups
Item models and scaling factors are registered in `client/scripts/ItemModelResolver.cs`:

```csharp
// Add to ModelMappings dictionary or register dynamically:
ItemModelResolver.RegisterItemModel('!', "res://assets/models/dungeon/potion_custom.glb", scale: 0.6f);
```

### 3. Adding Combat Feedback & Visual Effects
`DungeonWorld.cs` exposes clean hooks for juice and combat feedback:
- `SpawnFloatingText(string text, Vector3 worldPos, Color color, float scale = 1.0f)`: Spawns floating combat damage / status text.
- `AddTrauma(float amount)`: Applies kinetic screen shake that decays smoothly.
- `ProcessCombatEvents(JsonElement frame)`: Parses combat messages emitted by Angband to trigger floaters, sound effects, and camera trauma.

### 4. Updating the Engine Fork
If you make changes inside `engine/`:
1. Keep changes in `main-bridge.c`, `bridge-json.c`, `bridge-json.h`, or `BRIDGE_Frontend.cmake`.
2. Regenerate the engine patch:
   ```powershell
   cd engine; git diff 4.2.6..HEAD --output=..\engine-patch\0001-bridge-frontend.patch
   ```

## Debugging the bridge by hand

```powershell
python tools/bridge.py enter enter '@' '@' '@' '@'
```

Prints the screen after each keypress. For deeper work, drive it from Python:

```python
import sys; sys.path.insert(0, "tools")
from bridge import Bridge
b = Bridge(savefile="scratch")
b.birth()
b.wizard_on()
b.key(">")
b.debug("m")          # magic map
b.debug("u")          # detect all monsters
print(len(b.frame["monsters"]))
```

Wizard mode and the debug commands make most states reachable in seconds. The
full debug command list is in `engine/src/ui-game.c`, in the `cmd_debug_*`
arrays.

If the client seems to have no effect on the game, you have almost certainly hit
the frame synchronisation contract — see PROTOCOL.md.

## Writing a different client

The protocol is deliberately client-agnostic. A 2D, web or VR client is a
perfectly good contribution. `tools/bridge.py` is the reference implementation
and is small enough to port in an afternoon.

## Style & Conventions

- Engine code follows Angband's existing style: tabs, K&R braces, `/* */` comments.
- Client and tool code follows C# / Godot conventions: PascalCase methods/types, `_camelCase` private fields.
- Comment *why*, not *what*.

