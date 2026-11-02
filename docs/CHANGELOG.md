# Changelog

## M0 — Scaffold and plans (2026-11-02)

- Solution skeleton: `ArduSim.Core`, `Boards`, `Build`, `Ipc`, `Runner`, `App` and three xUnit v3 test projects on .NET 10, with central package management, analyzers and the brief's warning policy.
- WPF App shell with DI (Microsoft.Extensions.Hosting), Serilog file logging, dark theme tokens and Russian strings from `.resx`.
- Runner skeleton: `--version`; everything else fails loudly with "not implemented yet (M1)".
- Tests: project-boundary rules (Core/Boards/Build/Ipc stay UI-free and keep their allowed references) and Runner process tests.
- Draft firmware ABI `firmware/shim/include/sim_abi.h`.
- Toolchain evaluation kit `tools/toolchain-eval`: zig 0.17.0 vs llvm-mingw 20261020, both build a working x64 DLL that loads in .NET, calls through the ABI at ~5 ns per host call and symbolizes via dbghelp.
- Docs: ADR-001 … ADR-008, ARCHITECTURE, PLAN (with open questions), DECISIONS, CLAUDE.md, README, THIRD_PARTY.
