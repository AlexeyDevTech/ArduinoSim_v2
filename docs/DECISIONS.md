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
- 2026-11-02 — Shim `min`/`max` are templates in C++ (ArduinoCore-API style) so the C++ standard library stays usable; `round()` is the C library's (returns double; the AVR macro returns long).
- 2026-11-02 — `word` is `uint16_t` as on AVR, even though `int` is 32-bit.
- 2026-11-02 — `pgm_read_word`/`pgm_read_dword` through a pointer-typed address return the whole 64-bit pointer, so PROGMEM string tables work unchanged.
- 2026-11-02 — `EEMEM` variables fail at compile time with a clear message (they need linker-placed EEPROM addresses).
- 2026-11-02 — Direct port reads/writes cost one `digitalRead`/`digitalWrite` per changed bit (no ABI call for whole ports yet).
- 2026-11-02 — Libraries the shim provides (EEPROM now, Wire/SPI later) resolve before every other library root, since they are tied to the simulated hardware.
- 2026-11-02 — Library detection is textual (no preprocessor pass); an include inside an inactive `#if` still pulls its library in. Revisit if a bundled library trips on it.
- 2026-11-02 — Each return from `loop()` costs 1 µs of virtual time (main()'s loop overhead), so an empty `loop()` still lets time pass.
- 2026-11-02 — At most one ISR is dispatched per scheduling point, so a LOW-level interrupt cannot starve the host.
- 2026-11-02 — A run ends with `TerminateProcess` after flushing output, so the sketch's static destructors never call into a shutting-down runtime.
- 2026-11-02 — Headless runs default to `--speed max`; the watchdog ends a headless run that is busy without API calls for 2 s (exit code 3).
- 2026-11-02 — Runner exit codes: 0 ok, 1 build failed, 2 firmware error, 3 firmware busy, 64 usage, 69 toolchain unavailable, 70 internal error.
- 2026-11-02 — Build output goes to `%LOCALAPPDATA%\ArduSim\builds\<project>-<hash>` (override with `--build-dir`), keeping project folders clean.
- 2026-11-02 — The project file model lives in `ArduSim.Core.Projects` (plain data, System.Text.Json).
- 2026-11-02 — The toolchain is extracted with System.IO.Compression (no external tools); the first install takes about 2.5 minutes here, mostly extraction.
- 2026-11-02 — Integration test fixture projects live in `tests/ArduSim.Integration.Tests/Projects`; `examples/` holds only user-facing examples.
