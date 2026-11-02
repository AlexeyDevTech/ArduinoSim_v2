# Changelog

## M4 — LCD (2026-11-02)

- I2C: the board's SDA/SCL nets form a bus; a transfer reaches the device with that address on those nets, at 9 bit times per byte at the Wire clock. Nobody at an address is a NACK, as on hardware, with one warning that names the address and what is on the bus, or that the SDA/SCL wires of a device with that address are swapped or not on A4/A5. A note when the bus has only the MCU's weak pull-ups.
- Wire library in the shim: the AVR TwoWire API, 32-byte buffers, repeated start, `endTransmission` 0/2/3, `requestFrom` with and without a register address; slave mode warns that it is not simulated.
- HD44780 controller: full instruction set, DDRAM maps for 16x2 and 20x4, 8 CGRAM glyphs, 4-bit and 8-bit interfaces with the init sequence, busy flag and address counter reads, display/cursor shift, blinking cursor, the A00 character ROM. PCF8574 expander with the standard and mjkdz backpack wirings.
- LCD part: 16x2 or 20x4; I2C backpack, 4-bit or 8-bit parallel, with the pins following the choice; I2C address, backlight (blue, green, none) and contrast (from V0 when it is wired). It needs 5V and GND, reads an unconnected RW as "read" like the real controller, and warns about both. Palette presets: LCD 1602/2004, I2C or parallel.
- The glass is drawn dot by dot with the unlit grid visible, at the canvas zoom, so it stays sharp at every zoom level; frames travel in snapshots and identical frames are not redrawn.
- Bundled libraries LiquidCrystal 1.0.7 and LiquidCrystal_I2C 1.1.2; examples LcdHello, LcdI2c and LcdCustomChars. The libraries' own HelloWorld, Scroll and CustomCharacter examples run unmodified in the tests.
- The headless Runner prints each display's rows when they change (`lcd1: "hello, world!   " "3               "`).
- Builds show the sketch's warnings only: core and library headers are system headers, library sources compile without warnings.
- Fixed: a sketch whose global constructors wait (LiquidCrystal's calls `begin()`) started seconds late at 1×, because the run's clock started after the firmware had loaded.
- Fixed: errors while the app handled a message from the simulation were lost; they are logged now.

## M3 — Schematic editor (2026-11-02)

- Window per `docs/UI.md`: one 48 px title bar with the toolbar (project menu, Build/Run/Pause/Stop, step, speed, board, clock, status pill, snap-layout maximize), palette, center tabs, properties, bottom tabs and status bar; panels collapse to strips (Ctrl+1/2/3) and keep their sizes; a start view with recent projects and examples.
- Schematic canvas: wheel zoom to the cursor (25–800 %), pan with middle drag or Space, Fit and 100 %, 0.1" grid with snap; place from the palette by drag or double-click, select (click, Shift/Ctrl, marquee, Ctrl+A), move (Alt for free), rotate about the selection, flip, nudge, delete, copy/cut/paste/duplicate, context menus; everything undoable (Ctrl+Z/Ctrl+Y).
- Wires: click pin to pin with orthogonal routing (Tab toggles the bend, clicks add corners, Backspace removes one), drag a segment or a corner, double-click to add a corner, colors by net type or a chosen swatch, whole-net highlight and pin tooltips with the live level.
- While a run is active the scene is locked, but buttons, switches and pots stay live; parts return to rest when the run ends, warnings stay until the next run, and the Журнал tab counts unseen ones.
- Properties panel: rename a part, colors as swatches, numbers with units and range checks, choices, a key-capture field, the part's pins and connections; wire color; project settings when nothing is selected.
- Projects: new project (board plus an empty sketch), open, save (Ctrl+S, ● in the title while unsaved), unsaved-changes prompt, ten recent projects, autosave on Run.
- Code editor: AvalonEdit tabs with C++ highlighting, line numbers, auto-indent, bracket matching, red/amber squiggles and gutter marks, hover messages, F8/Shift+F8, click a diagnostic to jump; files reload when changed outside, with a conflict bar when both sides changed. The crash banner opens the crashing line.
- Simulation log messages are shown in Russian: Core keeps the English text and the arguments, the App fills its own pattern.

## M2 — Simulation core and first UI (2026-11-02)

- Circuit model: wires and switches form nets; outputs, rails, pull-ups, resistors and potentiometer wipers resolve to voltages; floating inputs, undefined levels and short circuits are reported once, pointing at the part.
- Parts: LED, RGB LED, pushbutton, slide switch, potentiometer, resistor, piezo buzzer and the Nano board (rails, on-board L LED). The MCU drives and reads its nets, so buttons, pull-ups and interrupts behave as wired.
- IPC: MessagePack over a named pipe, one Runner process per run, snapshots as deltas at up to 60 Hz, pause, resume, step by `loop()` or time, speed, part and Serial input; an in-process session for debugging (`--inproc`).
- Crash reports: a CPU fault in the sketch, such as a null dereference, ends only the Runner; the app shows the exception, the sketch line and the last 20 Arduino API calls, with Rerun. Firmware stuck without API calls is reported with Stop.
- App: dark shell with examples, Build/Run/Pause/Stop/Step/speed, the live scene, Serial Monitor with input and line endings, build output, simulation log with "show part", and a first-run toolchain download.
- Examples Button (INPUT_PULLUP toggles an LED) and FloatingInput (warns about the floating pin); Blink drives an external LED as well.

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
