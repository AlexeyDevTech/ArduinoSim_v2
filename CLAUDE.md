# CLAUDE.md

ArduSim: a Windows desktop simulator for Arduino sketches (C#/.NET 10, WPF). The full specification is the owner's brief; architecture is in `docs/ARCHITECTURE.md`, decisions in `docs/adr/` and `docs/DECISIONS.md`, the milestone plan in `docs/PLAN.md`.

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
dotnet run --project src/ArduSim.App          # WPF app
dotnet run --project src/ArduSim.Runner -- --version
powershell -ExecutionPolicy Bypass -File tools/toolchain-eval/Measure-Toolchains.ps1 -WorkDir <dir>   # ADR-002 numbers
```

Requires the .NET 10 SDK (10.0.100 or later feature band). No Arduino IDE, no arduino-cli.

## Repository map

```
ArduSim.sln, Directory.Build.props, Directory.Packages.props, global.json, .editorconfig
src/ArduSim.Core       engine: virtual clock, nets, buses, components, display controllers (no UI, no IPC)
src/ArduSim.Boards     board profiles and timing tables (refs Core)
src/ArduSim.Build      toolchain manager, .ino preprocessor, library resolver, compiler driver, object cache
src/ArduSim.Ipc        message contracts, named-pipe transport, ISimulationSession
src/ArduSim.Runner     console host per run: loads firmware.dll, hosts Core, serves IPC, headless CLI
src/ArduSim.App        WPF UI (refs Runner for the in-process session and to ship Runner.exe)
firmware/shim          C++ Arduino core shim; include/sim_abi.h is the firmware ABI (draft v1)
libraries/             vendored Arduino libraries; libraries/compat/ per-library fixes
examples/              example projects, each built and run by the integration tests
tests/                 ArduSim.Core.Tests, ArduSim.Build.Tests, ArduSim.Integration.Tests
tools/                 dev-only: toolchain-eval (ADR-002), nano-timing-bench (ADR-005 calibration)
docs/                  ARCHITECTURE, PLAN, DECISIONS, CHANGELOG, adr/ (UI.md before M3)
```

## Conventions

- C#: nullable on, file-scoped namespaces, records for messages, analyzers at `latest-recommended`, `TreatWarningsAsErrors` in Core, Build, Ipc. No logic in code-behind beyond view glue. Pass `CultureInfo.InvariantCulture` for machine-readable text (the owner's locale is ru-RU: decimal comma).
- Project boundaries are enforced by `tests/ArduSim.Core.Tests/ProjectBoundaryTests.cs`; update its allow-list only together with `docs/ARCHITECTURE.md`.
- UI strings only in `src/ArduSim.App/Resources/Strings.resx` (Russian), used via `{l:Loc Key}` / `Strings.Get`.
- NuGet licenses: MIT, BSD, Apache or Microsoft only; list every dependency in `THIRD_PARTY.md`. Versions only in `Directory.Packages.props`.
- C++ shim: C++17, warning-clean under clang (`-Wall -Wextra`), one `.cpp` per module, no `<windows.h>` in sketch-visible headers.
- Vendored libraries are never patched silently; fixes go to `libraries/compat/`.
- Conventional commits on `main`.

## Known limitations (current state: M0)

- Nothing simulates yet: the Runner only answers `--version`; the App shows a placeholder window.
- Toolchain, IPC serializer, docking, SSD1306 scroll scope, LCD modelling, timing table, ABI v1 and project schema v1 are proposals waiting for the owner (`docs/PLAN.md`, "Open questions").
- Toolchain measurements were taken on the dev machine with a scrubbed PATH, not on a clean VM.
- `tools/nano-timing-bench` is only syntax-checked with clang against a stub `Arduino.h`; it has not been compiled for AVR (no avr-gcc on this machine).
