# Changelog

## M1 — Build and run headless (2026-11-02)

- Toolchain bootstrap: llvm-mingw 20261020 is downloaded on first use into `%LOCALAPPDATA%\ArduSim\toolchain`, checked against its SHA-256, extracted without other architectures, and smoke-tested.
- Build pipeline: `.ino` preprocessing with prototypes and `#line` mapping, Arduino-style library resolution, parallel compiles with a per-unit object cache, linking `firmware.dll` + PDB, parsed diagnostics. A one-line change rebuilds in about 0.4 s.
- C++ Arduino shim for the Nano: core API, `String`, `Print`/`Stream`, `Serial`, `EEPROM`, pgmspace and AVR compatibility headers, port and register proxies; a standalone harness with 103 checks.
- Core engine: nanosecond virtual clock with an ordered event queue, `delay()` slicing, pacing (0.1×–10×, max), and an MCU model with pins, PWM, tone, ADC noise for unconnected inputs, a UART with a 64-byte TX ring drained at the baud rate, EEPROM with programming time persisted to `eeprom.bin`, and external interrupts.
- Runner: loads `firmware.dll` through the ABI v1 table (static constructors included), headless CLI `--project --run-for --speed --dump-serial --seed --quiet --build-dir`, a watchdog for firmware that stops calling the API, exit codes.
- `examples/Blink`; integration tests for Blink timing, Serial baud timing, static constructors, prototypes with default arguments, EEPROM persistence, floating-input warnings, build errors and runaway firmware.

## M0 — Scaffold and plans (2026-11-02)

- Solution skeleton: `ArduSim.Core`, `Boards`, `Build`, `Ipc`, `Runner`, `App` and three xUnit v3 test projects on .NET 10, with central package management, analyzers and the brief's warning policy.
- WPF App shell with DI (Microsoft.Extensions.Hosting), Serilog file logging, dark theme tokens and Russian strings from `.resx`.
- Runner skeleton: `--version`; everything else fails loudly with "not implemented yet (M1)".
- Tests: project-boundary rules (Core/Boards/Build/Ipc stay UI-free and keep their allowed references) and Runner process tests.
- Draft firmware ABI `firmware/shim/include/sim_abi.h`.
- Toolchain evaluation kit `tools/toolchain-eval`: zig 0.17.0 vs llvm-mingw 20261020, both build a working x64 DLL that loads in .NET, calls through the ABI at ~5 ns per host call and symbolizes via dbghelp.
- Docs: ADR-001 … ADR-008, ARCHITECTURE, PLAN (with open questions), DECISIONS, CLAUDE.md, README, THIRD_PARTY.
