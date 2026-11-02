# Decisions

Small defaults chosen without asking (brief §1). One line each; anything expensive to undo is an ADR in `docs/adr/` instead.

- 2026-11-02 — Solution file is classic `ArduSim.sln` (as named in the brief), not `.slnx`.
- 2026-11-02 — `global.json` pins SDK 10.0.100 with `latestFeature` roll-forward; all projects target `net10.0` (`net10.0-windows` for the App).
- 2026-11-02 — NuGet versions are managed centrally in `Directory.Packages.props`; shared MSBuild settings in `Directory.Build.props`.
- 2026-11-02 — Analyzers at `latest-recommended` everywhere; `TreatWarningsAsErrors` only in Core, Build and Ipc (brief). CA2007 (ConfigureAwait) is off: this is an application.
- 2026-11-02 — Tests use xUnit v3 on Microsoft.Testing.Platform (`global.json` `test.runner`), as required by the .NET 10 SDK for xUnit v3; run with `dotnet test`.
- 2026-11-02 — `ArduSim.Build.Tests` ignores the "zero tests" exit code until its first test lands in M1.
- 2026-11-02 — The App references the Runner project: it hosts the in-process session and puts `ArduSim.Runner.exe` next to the App.
- 2026-11-02 — `ISimulationSession` lives in `ArduSim.Ipc` next to the contracts it uses.
- 2026-11-02 — Virtual time is kept in nanoseconds internally (`ulong`), exposed to firmware in µs.
- 2026-11-02 — UI strings: `Resources/Strings.resx` with Russian as the neutral language, read through `{l:Loc Key}` and `Strings.Get`; missing keys render as `!Key!`.
- 2026-11-02 — Icon set: Fluent UI System Icons (MIT), Regular style, as path geometry.
- 2026-11-02 — UI font: Segoe UI Variable with Segoe UI fallback; nothing bundled.
- 2026-11-02 — Logging: `Microsoft.Extensions.Logging` API with Serilog as provider; log files are written with the invariant culture.
- 2026-11-02 — Firmware debug info is CodeView/PDB, symbolized with the in-box `dbghelp.dll` (without `SYMOPT_DEFERRED_LOADS`, which breaks line lookup for these PDBs).
- 2026-11-02 — The shim never exposes `<windows.h>` to sketch translation units.
- 2026-11-02 — Dev-only scripts live in `tools/` (toolchain evaluation, Nano timing bench); they are not part of the build or tests.
- 2026-11-02 — Git: `main` branch, conventional commits.
- 2026-11-02 — The brief is kept in the repo as `docs/BRIEF.md` so a clean clone carries its own specification.
