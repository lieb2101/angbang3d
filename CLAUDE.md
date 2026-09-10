# CLAUDE.md — Anthropic Claude & Claude Code Guidance for angband3d

See `AGENTS.md` and `docs/LLM_CONTEXT.md` for full architectural documentation.

## Essential Commands
- **Build Engine**: `$env:MSYSTEM='MINGW64'; $env:CHERE_INVOKING='1'; & C:\msys64\usr\bin\bash.exe -lc "cd /c/Dev/angband3d/engine && cmake --build build"`
- **Build Client**: `dotnet build client/angband3d.csproj`
- **Smoke Tests**: `python tools/smoke_test.py`
- **Play Game**: `.\play.cmd` or `.\play.ps1 -Character <name> -Random`

## Fast Orientation & Crash Recovery
1. Run `git status`.
2. Review `docs/NEXT_STEPS.md` to see where the previous session left off.
3. Validate health with `python tools/smoke_test.py` and `dotnet build client/angband3d.csproj`.

## Token & Context Optimization Rules
- Never run unbounded searches or cat massive files.
- Refer to `AGENTS.md` section 4 for exact file paths.
- Perform targeted replacements rather than full-file writes.
- Keep responses compact, factual, and direct.

## Invariants
- Upstream Angband C changes must stay in `main-bridge.c` / `bridge-json.c`. Always update `engine-patch/0001-bridge-frontend.patch` with `git diff 4.2.6..HEAD --output=..\engine-patch\0001-bridge-frontend.patch`.
- Use `bridge_get_equipped_by_type()` (not `slot_by_name()`).
- Gate grid reads on `bridge_in_play()`.
- Do not use Godot's `OS.ExecuteWithPipe()` (deadlocks); `BridgeClient.cs` uses `System.Diagnostics.Process`.
