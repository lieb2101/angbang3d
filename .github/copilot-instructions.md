# angband3d — GitHub Copilot & AI Agent Operating Instructions

Before doing work, read `AGENTS.md` and `docs/LLM_CONTEXT.md`.

## 1. Fast Orientation & Crash Recovery
When starting a session or recovering after a crash/interruption:
1. `git status` — inspect current branch and working copy.
2. Read `docs/NEXT_STEPS.md` — find current execution queue and state.
3. Validate state with `python tools/smoke_test.py` and `dotnet build client/angband3d.csproj`.
4. Proceed directly to the next pending item without redundant discovery queries.

## 2. Token, Context & Rate Limit Discipline
- **Zero Hallucinated Searches**: Do not run wide glob searches for standard classes; use the File Map in `AGENTS.md` / `docs/LLM_CONTEXT.md`.
- **Targeted Code Reads**: Read only 20-80 line windows containing target logic.
- **Surgical Edits**: Use targeted replacements with ~3 lines of surrounding context. Avoid rewriting whole files.
- **Concise Responses**: Output concise, dense, action-first responses. Avoid echoing large boilerplate.

## 3. Critical Invariants
- **Engine Isolation**: Engine changes live only in `engine/src/main-bridge.c`, `bridge-json.c`, `bridge-json.h`. Regenerate patch with:
  `cd engine; git diff 4.2.6..HEAD --output=..\engine-patch\0001-bridge-frontend.patch` (never use `>`).
- **Safe Equipment Lookup**: Use `bridge_get_equipped_by_type(player, EQUIP_*)` instead of `slot_by_name()`.
- **Level Gen Guard**: Access to `cave` or `player->grid` must be guarded by `bridge_in_play()`.
- **Input & Motion**:
  - Movement: Send `up`/`down`/`left`/`right` arrow keys. (Digits are repeat prefixes in Angband).
  - Turning: Camera yaw only, 0-turn, non-blocking.
  - View routing: Governed by `NeedsTerminal(frame)` in `Main.cs`.

## 4. Standard Commands
- Build Engine: `$env:MSYSTEM='MINGW64'; $env:CHERE_INVOKING='1'; & C:\msys64\usr\bin\bash.exe -lc "cd /c/Dev/angband3d/engine && cmake --build build"`
- Build Client: `dotnet build client/angband3d.csproj`
- Smoke Tests: `python tools/smoke_test.py`
- Play: `.\play.cmd` or `.\play.ps1 -Character <name> -Random`
