# angband3d — Coding & Architecture Instructions

Before making any changes to this codebase, read `docs/LLM_CONTEXT.md`.

## Core Philosophy
1. **Engine Rebasability**: Keep changes to upstream Angband isolated to `engine/src/main-bridge.c` and `engine/src/bridge-json.c`. After editing engine code, refresh `engine-patch/0001-bridge-frontend.patch`.
2. **Deterministic Verification**:
   - Always run `python tools/smoke_test.py` to verify bridge communication and state serialization.
   - Run `dotnet build client/angband3d.csproj` to verify Godot C# compilation.
3. **Efficiency & Rate Limits**:
   - Use precise, minimal edits (`replace_string_in_file`).
   - Do not perform redundant broad codebase searches when file paths are already documented in `docs/LLM_CONTEXT.md`.
4. **Input & View Rules**:
   - `NeedsTerminal` in `Main.cs` governs whether the terminal overlay or 3D world is drawn. Do NOT alter this logic without reviewing `docs/LLM_CONTEXT.md`.
   - Never gate camera turning (Left/Right) on engine busy state or turn passage.
