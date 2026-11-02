# CLAUDE.md

ArduSim: a Windows desktop simulator for Arduino sketches (C#/.NET 10, WPF). The full specification is the owner's brief, `docs/BRIEF.md` (copy of 2026-11-02; a newer brief from the owner replaces it). Architecture is in `docs/ARCHITECTURE.md`, decisions in `docs/adr/` and `docs/DECISIONS.md`, the milestone plan in `docs/PLAN.md`.

## Working agreement

- Talk to the owner in Russian. Code, identifiers, comments, commit messages and all docs are in English.
- Brief §3 decisions are final: implement them, record them as ADRs, do not re-argue. Other defaults: pick one, add a line to `docs/DECISIONS.md`. Ask only at expensive forks (file formats, firmware ABI, IPC protocol, UI framework), batched.
- Work milestone by milestone. Done = clean clone builds, `dotnet test` green, app runs, `docs/CHANGELOG.md` entry, two-line "how to try it".
- Before M3: write `docs/UI.md` and wait for the owner's OK.
- Never fake behaviour: stubs log a visible warning; unsupported features fail at compile time with a clear message.
- Nothing from brief §4 "Out of scope" without asking.

## Commands

```
dotnet build ArduSim.sln
dotnet test                                   # Microsoft.Testing.Platform runner (see global.json)
dotnet run --project src/ArduSim.App          # WPF app (placeholder window until M2)
src/ArduSim.Runner/bin/Debug/net10.0/ArduSim.Runner.exe --project examples/Blink --run-for 3000
powershell -ExecutionPolicy Bypass -File tools/toolchain-eval/Measure-Toolchains.ps1 -WorkDir <dir>   # ADR-002 numbers
```

Requires the .NET 10 SDK. The first build of any sketch (or the first `dotnet test`) downloads the pinned llvm-mingw (182 MiB) into `%LOCALAPPDATA%\ArduSim\toolchain`; that takes a few minutes once. No Arduino IDE, no arduino-cli.

Runner options: `--project <folder>` (required), `--run-for <ms>`, `--speed <x|max>` (default max), `--dump-serial <file>`, `--seed <n>`, `--quiet`, `--build-dir <folder>`. Output lines are `[   1000.008 ms] D13 LOW`, `[ ... ms] Serial: text`, `[ ... ms] warning: ...`. Exit codes are in `src/ArduSim.Runner/ProcessExit.cs`.

## Repository map

```
ArduSim.sln, Directory.Build.props, Directory.Packages.props, global.json, .editorconfig
src/ArduSim.Core       engine: Timing (clock, pacer), Simulation (engine, Mcu, UART, EEPROM, interrupts), Diagnostics, Boards (profile types), Projects (project.simproj.json)
src/ArduSim.Boards     board profiles and timing tables (Nano)
src/ArduSim.Build      Toolchains (manager, manifest), Sketches (preprocessor), Libraries (resolver), Compilation (builder, object cache, diagnostics)
src/ArduSim.Ipc        message contracts, named-pipe transport, ISimulationSession (from M2)
src/ArduSim.Runner     Interop (SimHostApi mirror, callbacks, firmware loader), SimulationHost, Headless (CLI)
src/ArduSim.App        WPF UI (refs Runner for the in-process session and to ship Runner.exe)
firmware/shim          C++ Arduino shim: include/ (Arduino.h, sim_abi.h, avr/, sim/), src/, variants/nano, libraries/EEPROM, test/ (harness)
libraries/             vendored Arduino libraries (from M4); libraries/compat/ per-library fixes
examples/              user-facing example projects (Blink)
tests/                 ArduSim.Core.Tests, ArduSim.Build.Tests, ArduSim.Integration.Tests (+ Projects/ fixture sketches)
tools/                 dev-only: toolchain-eval (ADR-002), nano-timing-bench (ADR-005 calibration)
docs/                  BRIEF (spec), ARCHITECTURE, PLAN, DECISIONS, CHANGELOG, adr/ (UI.md before M3)
```

`firmware/` and `libraries/` are copied next to every executable that builds firmware (content items in `ArduSim.Build.csproj`).

## Conventions

- C#: nullable on, file-scoped namespaces, records for messages, analyzers at `latest-recommended`, `TreatWarningsAsErrors` in Core, Build, Ipc. No logic in code-behind beyond view glue. Pass `CultureInfo.InvariantCulture` for machine-readable text (the owner's locale is ru-RU: decimal comma).
- Static readonly fields are initialized in textual order: declare what an initializer uses above it.
- Project boundaries are enforced by `tests/ArduSim.Core.Tests/ProjectBoundaryTests.cs`; update its allow-list only together with `docs/ARCHITECTURE.md`.
- UI strings only in `src/ArduSim.App/Resources/Strings.resx` (Russian), used via `{l:Loc Key}` / `Strings.Get`.
- NuGet licenses: MIT, BSD, Apache or Microsoft only; list every dependency in `THIRD_PARTY.md`. Versions only in `Directory.Packages.props`.
- C++ shim: C++17, warning-clean under clang `-Wall -Wextra -Werror` (the harness test enforces it), one `.cpp` per module, `<windows.h>` only in `src/host_bootstrap.cpp`. ABI changes: `sim_abi.h` and `src/ArduSim.Runner/Interop/SimHostApi.cs` together, same field order.
- Host callbacks never let an exception reach native code (each one catches and ends the run with an internal-error report).
- Vendored libraries are never patched silently; fixes go to `libraries/compat/`.
- Conventional commits on `main`.

## Known limitations (current state: M1)

- No nets or components yet (M2): an input without `INPUT_PULLUP` floats (warning, random reads) and `analogRead` returns noise.
- I2C and SPI are not simulated and there is no `Wire`/`SPI` library yet (M4/M5): sketches that include them fail to compile.
- No crash report yet (M2): a null dereference in the sketch kills the Runner with .NET's fatal-error output.
- The UI is still the M0 placeholder window.
- Timer, ADC and USART registers are not simulated (warning on first write, read 0); direct port access costs 4 µs per bit.
- The Nano timing table v1 is partly estimated; calibration against real hardware or avr8js is still open (ADR-005).
- Toolchain measurements were taken on the dev machine with a scrubbed PATH, not on a clean VM.
- `tools/nano-timing-bench` is only syntax-checked with clang against a stub `Arduino.h`; it has not been compiled for AVR (no avr-gcc on this machine).
