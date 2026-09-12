# AGENTS.md — Autonomous Agent Operating Guidelines for angband3d

## 1. Zero-Turn Orientation Protocol (Crash Recovery & Context Resumption)
If starting a new session or recovering from a crash/disconnect:
1. Run `git status` to see unstaged work and active branch.
2. Check `docs/NEXT_STEPS.md` for the current priority queue and in-flight tasks.
3. Check `docs/LLM_CONTEXT.md` for architectural invariants and file paths.
4. Verify workspace health:
   - `python tools/smoke_test.py` (11/11 tests, checks engine JSON bridge)
   - `dotnet build client/angband3d.csproj` (Godot C# compilation)
5. **DO NOT** execute exploratory recursive file searches (`**/*`) or dump entire files into context. All key file paths and data structures are indexed in `docs/LLM_CONTEXT.md`.

## 2. Token, Efficiency & Workflow Rules
- **Targeted Reads**: Read only the relevant line ranges (20-100 lines) around target symbols.
- **Surgical Edits**: Use exact string replacement tools (`multi_replace_string_in_file` / `replace_string_in_file`) with 3 lines of context. Avoid rewriting full files.
- **Avoid Command Polling**: Never run sleep loops or wait scripts in terminals. One-shot sync commands are standard.
- **Output Brevity**: Keep chat responses short, dense, and action-oriented. State the fix, verify it, and record progress.
- **Meaningful Code Comments**: Add clear, descriptive comments along the way for non-obvious logic, coordinate transformations, protocol changes, and invariant guards.
- **Continuous Plan Tracking**: Keep track of in-flight goals, subtasks, and progress by maintaining `docs/NEXT_STEPS.md` and memory notes after completing milestones.

## 3. Strict Architectural Invariants
1. **Engine Rebasability**:
   - Upstream Angband 4.2.6 C code lives in `engine/`. Never alter game mechanics or RNG.
   - Bridge code is strictly contained in `engine/src/main-bridge.c`, `engine/src/bridge-json.c`, and `engine/src/bridge-json.h`.
   - After modifying engine files, re-export the patch:
     ```powershell
     cd engine; git diff 4.2.6..HEAD --output=..\engine-patch\0001-bridge-frontend.patch
     ```
     *(Note: Always use `--output=`, never `>` which produces broken UTF-16 on Windows).*
2. **Safe Equipment & State Querying**:
   - Never call `slot_by_name()` on optional slots (it asserts and crashes the engine). Use `bridge_get_equipped_by_type(player, EQUIP_*)`.
   - Never query dungeon coordinates without `bridge_in_play()` check (guards against uninitialized grids during level gen).
3. **Input Protocol**:
   - Movement = Arrow keys (`up`/`down`/`left`/`right`). Never send raw digits (digits are repeat counts in Angband keyset).
   - Turning = Client-side camera yaw only, instant, 0 game turns.
   - View routing = Governed by `NeedsTerminal(frame)` in `Main.cs`:
     - `phase != "play"` or `ui.overlay > 0` or (`!awaiting_command && !more`) -> Terminal View.
     - `more == true` (-more- prompt) -> Stay in 3D View, show message banner.

## 4. Key File Map
| Domain | File Path | Role |
|---|---|---|
| Engine Bridge | `engine/src/main-bridge.c` | JSON state serialization & stdin keypress dispatcher |
| JSON Helpers | `engine/src/bridge-json.c` | Fast, zero-alloc JSON emitter |
| Process Stdio | `client/scripts/BridgeClient.cs` | Manages `System.Diagnostics.Process` stdio streams |
| Main Coordinator | `client/scripts/Main.cs` | View routing, input handling, scene lifecycle |
| 3D World | `client/scripts/DungeonWorld.cs` | Procedural grid mesh, camera tweening, torchlight, VFX |
| Viewmodel Hands | `client/scripts/ViewModel.cs` | Dynamic first-person hands, weapon rigs, race/height scaling |
| Monster Models | `client/scripts/MonsterModelResolver.cs` | Monster glyph/name -> 3D model & animation rules |
| Item Models | `client/scripts/ItemModelResolver.cs` | Item glyph/name -> 3D pickup models & materials |
| Audio | `client/scripts/AudioManager.cs` | Positional SFX (footsteps, impacts, spells, doors) |
| 2D Overlays | `client/scripts/Overlay.cs` | HUD, 2D Minimap, 80x24 Terminal, Menus |
| Wire Spec | `docs/PROTOCOL.md` | Complete JSON protocol documentation |
| Task Roadmap | `docs/NEXT_STEPS.md` | Living task queue and session notes |

## 5. Build & Verification Commands
- **Engine Build**:
  ```powershell
  $env:MSYSTEM='MINGW64'; $env:CHERE_INVOKING='1'
  & C:\msys64\usr\bin\bash.exe -lc "cd /c/Dev/angband3d/engine && cmake --build build"
  ```
- **Client Build**: `dotnet build client/angband3d.csproj`
- **Smoke Tests**: `python tools/smoke_test.py`
- **Launch Game**: `.\play.cmd` (or `.\play.ps1 -Character <name> [-Random|-Manual]`)
