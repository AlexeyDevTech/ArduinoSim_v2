# CLAUDE.md

ArduSim: a Windows desktop simulator for Arduino sketches (C#/.NET 10, WPF). The full specification is the owner's brief, `docs/BRIEF.md` (copy of 2026-11-02; a newer brief from the owner replaces it). Architecture is in `docs/ARCHITECTURE.md`, decisions in `docs/adr/` and `docs/DECISIONS.md`, the milestone plan in `docs/PLAN.md`.

## Working agreement

- Talk to the owner in Russian. Code, identifiers, comments, commit messages and all docs are in English.
- Brief §3 decisions are final: implement them, record them as ADRs, do not re-argue. Other defaults: pick one, add a line to `docs/DECISIONS.md`. Ask only at expensive forks (file formats, firmware ABI, IPC protocol, UI framework), batched.
- Work milestone by milestone. Done = clean clone builds, `dotnet test` green, app runs, `docs/CHANGELOG.md` entry, two-line "how to try it".
- The UI follows `docs/UI.md` (approved 2026-11-02); changes to its layout or interaction rules go to the owner first.
- Never fake behaviour: stubs log a visible warning; unsupported features fail at compile time with a clear message.
- Nothing from brief §4 "Out of scope" without asking.

## Commands

```
dotnet build ArduSim.sln
dotnet test                                   # Microsoft.Testing.Platform runner (see global.json)
dotnet run --project src/ArduSim.App          # WPF app; add -- --inproc to run the simulation in-process (debugging)
src/ArduSim.Runner/bin/Debug/net10.0/ArduSim.Runner.exe --project examples/Blink --run-for 3000
powershell -ExecutionPolicy Bypass -File tools/toolchain-eval/Measure-Toolchains.ps1 -WorkDir <dir>   # ADR-002 numbers
```

Requires the .NET 10 SDK. The first build of any sketch (or the first `dotnet test`) downloads the pinned llvm-mingw (182 MiB) into `%LOCALAPPDATA%\ArduSim\toolchain`; that takes a few minutes once. No Arduino IDE, no arduino-cli.

Runner options: `--project <folder>` (required), `--run-for <ms>`, `--speed <x|max>` (default max), `--dump-serial <file>`, `--seed <n>`, `--input <ms>:<part>:<name>=<value>` (repeatable), `--quiet`, `--build-dir <folder>`. Output lines are `[   1000.008 ms] D13 LOW`, `[ ... ms] led1 brightness=1`, `[ ... ms] Serial: text`, `[ ... ms] warning: ...`. Exit codes are in `src/ArduSim.Runner/ProcessExit.cs`. `--ipc ...` is the mode the app uses (PipeSimulationSession passes the arguments).

## Repository map

```
ArduSim.sln, Directory.Build.props, Directory.Packages.props, global.json, .editorconfig
src/ArduSim.Core       engine: Timing, Circuits (nets), Components (+ Parts), Simulation (engine, Mcu, BoardSimulation, UART, EEPROM, interrupts), Diagnostics, Boards (profile types), Projects (file, template), Editing (SceneModel, SceneEditor)
src/ArduSim.Boards     board profiles and timing tables (Nano)
src/ArduSim.Build      Toolchains, Sketches (preprocessor), Libraries (resolver), Compilation (builder, object cache, diagnostics, FirmwareSymbols)
src/ArduSim.Ipc        Messages (MessagePack), IpcChannel, ISimulationSession, PipeSimulationSession, CrashBlock
src/ArduSim.Runner     Interop (SimHostApi mirror, callbacks, loader), SimulationHost, Session (SessionServer, PipeServer, InProcessSimulationSession), Headless
src/ArduSim.App        WPF UI: MainWindow (chrome, panels), Views (SchematicCanvas, CodeEditor, UnsavedDialog, converters), ViewModels (Main, Scene, Palette, Properties, CodeDocument), Services (build, environment, UserState), Themes (Dark, Controls, Parts), Localization (Strings, LogText), Resources/Strings.resx
firmware/shim          C++ Arduino shim: include/ (Arduino.h, sim_abi.h, avr/, sim/), src/, variants/nano, libraries/EEPROM, test/ (harness)
libraries/             vendored Arduino libraries (from M4); libraries/compat/ per-library fixes
examples/              user-facing example projects (Blink, Button, FloatingInput); copied next to the app
tests/                 ArduSim.Core.Tests, ArduSim.Build.Tests, ArduSim.Integration.Tests (+ Projects/ fixture sketches)
tools/                 dev-only: toolchain-eval (ADR-002), nano-timing-bench (ADR-005 calibration)
docs/                  BRIEF (spec), ARCHITECTURE, UI, PLAN, DECISIONS, CHANGELOG, adr/
```

`firmware/` and `libraries/` are copied next to every executable that builds firmware (content items in `ArduSim.Build.csproj`).

## Conventions

- C#: nullable on, file-scoped namespaces, records for messages, analyzers at `latest-recommended`, `TreatWarningsAsErrors` in Core, Build, Ipc. No logic in code-behind beyond view glue. Pass `CultureInfo.InvariantCulture` for machine-readable text (the owner's locale is ru-RU: decimal comma).
- Static readonly fields are initialized in textual order: declare what an initializer uses above it.
- Project boundaries are enforced by `tests/ArduSim.Core.Tests/ProjectBoundaryTests.cs`; update its allow-list only together with `docs/ARCHITECTURE.md`.
- UI strings only in `src/ArduSim.App/Resources/Strings.resx` (Russian), used via `{l:Loc Key}` / `Strings.Get`. Simulation log messages: Core reports interpolated strings with a code (and a variant when the code is shared); add the Russian pattern as `Log_<code>[.<variant>]`.
- Scene edits go through `SceneEditor` (Core, unit-tested); the canvas only translates gestures. Part art: one `Part.<type>` template in `Themes/Parts.xaml`.
- NuGet licenses: MIT, BSD, Apache or Microsoft only; list every dependency in `THIRD_PARTY.md`. Versions only in `Directory.Packages.props`.
- C++ shim: C++17, warning-clean under clang `-Wall -Wextra -Werror` (the harness test enforces it), one `.cpp` per module, `<windows.h>` only in `src/host_bootstrap.cpp`. ABI changes: `sim_abi.h` and `src/ArduSim.Runner/Interop/SimHostApi.cs` together, same field order.
- Host callbacks never let an exception reach native code (each one catches and ends the run with an internal-error report).
- Vendored libraries are never patched silently; fixes go to `libraries/compat/`.
- Conventional commits on `main`.

## Known limitations (current state: M3)

- Not in the UI yet: Settings dialog, the "О точности симуляции" sheet, live levels in the properties pin list, common properties for a multi-selection, the light theme. Only one board exists, so the board picker cannot switch boards.
- Canvas mouse gestures and palette drag and drop have no automated checks; the automated checks drive the view models and render the window offscreen.
- I2C and SPI are not simulated and there is no `Wire`/`SPI` library yet (M4/M5): sketches that include them fail to compile.
- Headless runs print only the shim's one-line crash notice and exit with the NTSTATUS; the symbolized report is the app's.
- In-process sessions (`--inproc`) cannot unwind a stopped run: its thread stays parked; a sketch crash takes the app down.
- The App is checked by rendering it offscreen (RenderTargetBitmap); there are no UI automation tests.
- Timer, ADC and USART registers are not simulated (warning on first write, read 0); direct port access costs 4 µs per bit.
- The Nano timing table v1 is partly estimated; calibration against real hardware or avr8js is still open (ADR-005).
- Toolchain measurements were taken on the dev machine with a scrubbed PATH, not on a clean VM.
- `tools/nano-timing-bench` is only syntax-checked with clang against a stub `Arduino.h`; it has not been compiled for AVR (no avr-gcc on this machine).
