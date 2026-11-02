# Plan

Milestones from the brief (§6). A milestone is done when a clean clone builds, `dotnet test` is green, the app runs, `docs/CHANGELOG.md` has an entry, and the owner has a two-line "how to try it".

## M0 — Scaffold and plans ✅

- [x] Solution skeleton (`ArduSim.sln`, 6 src + 3 test projects, central packages, analyzers)
- [x] ADR-001 … ADR-008, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, `CLAUDE.md`, README, THIRD_PARTY
- [x] Toolchain measurement and smoke build (`tools/toolchain-eval`), draft ABI `firmware/shim/include/sim_abi.h`
- [x] Owner answers the open questions (2026-11-02: all eight proposals accepted)

## M1 — Build and run headless ✅

Done when `ArduSim.Runner --project examples/Blink --run-for 3000` prints D13 toggles 1000 ms apart.

- [x] `ArduSim.Build` toolchain manager: manifest (URL, SHA-256, size), resumable download with progress, extraction filter, post-install smoke compile, typed failures for "re-download".
- [x] `.ino` preprocessor: concatenation order, `#include <Arduino.h>`, prototype generation (C++-aware scanner; skips `#if` blocks, templates, members, operators, declared names; moves default arguments into the prototype), `#line` mapping.
- [x] Library resolver: include scan, search order, `library.properties` layouts, `utility/`; shim libraries (EEPROM) first.
- [x] Compiler driver: parallel per-TU compiles, object cache keyed by content + header hashes, link skipped when inputs are unchanged, `firmware.dll` + PDB, clang/lld diagnostics parser.
- [x] Shim core: Arduino API, time, `String`, `Print`/`Stream`/`Printable`, `HardwareSerial`, `EEPROM` (+ `avr/eeprom.h`), `F()`/pgmspace, bit macros, avr-libc-compatible `random`, compat headers (`avr/io.h`, `interrupt.h`, `sleep.h`, `wdt.h`, `util/delay.h`, `util/atomic.h`), port and register proxies, entry points, static-constructor bootstrap; standalone harness (103 checks).
- [x] `ArduSim.Boards`: Nano profile, timing table v1, compiler macros.
- [x] Core: clock + event queue (ns), delay slicing, pacing, MCU model (pins, PWM, tone, ADC, UART with 64-byte TX ring and RX buffer, EEPROM with programming time, external interrupts), `eeprom.bin` persistence, project file loader.
- [x] Runner: firmware loader, ABI handshake, `SimHostApi` callbacks, headless CLI, watchdog, exit codes.
- [x] `examples/Blink` and integration tests (Blink, Serial baud timing, static constructors/prototypes/EEPROM/warnings, build errors, runaway, shim harness).
- Moved to M2: the firmware's vectored exception handler and the crash report — they need the App-side shared memory and symbolization to be useful.
## M2 — Simulation core and first UI ✅

Done when Blink blinks on screen, the Button example toggles an LED, and a floating input produces a warning.

- [x] Core circuit: nets from wires and closed switches, drivers, pulls, resistor coupling (Thevenin rules), rails, floating/undefined/short-circuit warnings pointing at parts; tests.
- [x] Nano board model on nets: pin modes, INPUT_PULLUP (35 kΩ), PWM duty and tone as net state, ADC from net voltage and reference, external interrupts from net edges, `noInterrupts`.
- [x] Parts: LED (color, PWM brightness), RGB LED, pushbutton (+ keyboard shortcut), slide switch, potentiometer, resistor, piezo buzzer (frequency), and the board (rails, on-board L LED); tests.
- [x] IPC: MessagePack contracts, framing, `Hello`, named pipe, `PipeSimulationSession`, in-process session (`--inproc`), snapshots ≤ 60 Hz as deltas, pause/resume/step (loop, time), speed, part inputs, Serial input; tests.
- [x] Crash reports: vectored exception handler in the shim, shared crash block with the last 20 host calls, dbghelp symbolization to `file:line`; runaway reports over IPC; tests.
- [x] App: shell with the fixed layout, the project's scene rendered live, toolbar (examples, open folder, Build, Run, Pause/Resume, Stop, Step loop/1/10/100 ms, speed, virtual time), Serial Monitor, Build output, Simulation log with "show part", crash/busy/toolchain banners, first-run toolchain download.
- [x] Examples Button and FloatingInput; Blink gains an external LED; headless `--input` for tests.
- Not yet: simulation log messages come from Core in English (they carry codes; localization with the rest of the UI work), pin tooltips and net highlighting (M3), Serial Plotter (M5).

## M3 — Schematic editor ✅

Starts only after `docs/UI.md` is approved. Done when the Blink+button scene can be built from scratch, saved, reopened and run.

- [x] `docs/UI.md`: wireframe, visual language, interaction rules → approved 2026-11-02.
- [x] Theme: tokens, control templates (buttons, tabs, inputs, lists, scrollbars, menus, tooltips), dark theme complete.
- [x] Canvas: infinite, pan (middle drag / Space), zoom to cursor, grid and snap, place/move/rotate/flip/delete, multi-select, copy/paste, undo/redo for everything.
- [x] Wires: pin-to-pin, orthogonal routing with manual waypoints, segment and corner drag, net-type colors, whole-net hover highlight, pin tooltips with live level.
- [x] Palette (search, categories, drag to canvas) and properties panel (id, colors, numbers with units, choices, key capture, pins; I2C address and sizes come with the parts that have them, M4/M5).
- [x] Project save/load (ADR-008), autosave on Run, recent projects, examples open as copies; new project, unsaved-changes prompt, start view.
- [x] Code editor: AvalonEdit tabs, C/C++ highlighting, line numbers, build error markers, go-to-error, Ctrl+B, FileSystemWatcher reload with conflict warning.
- [x] Simulation log messages translated by key (Core keeps English text plus arguments).
- Not yet: Settings dialog and the "О точности симуляции" sheet (M5 polish), live levels in the properties pin list (the canvas tooltip has them), common properties for a multi-selection, board switching (one board exists), the light theme.

## M4 — LCD

Done when HelloWorld shows on a 1602 over I2C and on a 2004 in 4-bit mode, and `createChar` and `scrollDisplayLeft` work.

1. I2C bus routing in Core; Wire shim (32-byte buffer, repeated start, `endTransmission` codes); tests.
2. Parallel bus sampling; HD44780 model (full instruction set, DDRAM mapping, CGRAM, busy flag/AC read, A00 ROM); PCF8574 model with bit maps; recorded-stream tests.
3. LCD component (size, interface, backlight variants, contrast) and renderer with dot grid.
4. Vendor LiquidCrystal and LiquidCrystal_I2C with pinned versions; examples LCD Hello (4-bit), LCD I2C; integration tests (row 0 "hello, world!", counter on row 1).

## M5 — OLED and polish (MVP)

Done when every item of brief §7 passes.

1. SPI bus (hardware byte path, bit-level edge path, CS gating) and SPI shim; tests.
2. SSD1306 (all addressing modes, remap/scan, multiplex, contrast, invert, scroll per the answer to Q4, variants) and SH1106 models; golden tests.
3. OLED component and renderer (glow, white/blue/yellow-blue two-tone, contrast, display off).
4. Vendor Adafruit_GFX, Adafruit_BusIO, Adafruit_SSD1306, Adafruit_SH110X, U8g2, GyverOLED; compat shims where a library has no portable path; examples OLED Adafruit, OLED U8g2, OLED GyverOLED, SH1106; integration tests (no NACKs, non-empty framebuffer at frame N).
5. Serial Plotter, light theme, examples gallery, "About simulation accuracy" panel, README.
6. Acceptance pass on a clean Windows VM; performance checks (canvas 60 fps with 50 parts/200 wires, ≥ 2 M host calls/s, ≤ 300 MB working set).

## Risks

| Risk | Mitigation |
| --- | --- |
| Prototype generation misses arduino-cli corner cases | Port arduino-cli's documented test cases into `ArduSim.Build.Tests`; fail with a clear message pointing at the line instead of guessing. |
| A bundled library has no portable code path | Per-library compat shim in `libraries/compat/`, found in M4/M5 by the integration tests. |
| Warm rebuild over 3 s with big libraries (U8g2) | Object cache + parallel compile; measured per example in integration tests. |
| Toolchain fails on a clean machine | Post-install smoke compile with "re-download"; clean-VM pass in M5. |

## M0 questions (resolved 2026-11-02: all proposals accepted)

Kept for the record; each proposal below is now the decision in its ADR.

1. **Toolchain** (ADR-002): llvm-mingw 20261020 UCRT, 182 MiB download, ~0.45 s per TU and ~0.15 s link — or zig 0.17.0, 96 MiB but ~0.8 s per TU, ~0.5 s link and +31 s on the very first build? **Proposal: llvm-mingw.**
2. **IPC serializer** (ADR-003): **Proposal: MessagePack-CSharp** (inspectable, tolerant evolution, active); MemoryPack's speed is irrelevant at ≤ 60 Hz.
3. **Docking** (ADR-004): **Proposal: custom split layout**; AvalonDock is MS-PL and would need full re-theming for our look.
4. **SSD1306 scrolling** (ADR-006): **Proposal: full scroll command set**, because the acceptance example uses the diagonal scrolls.
5. **LCD with backpack** (ADR-006): **Proposal: one component with an `interface` property**, as you lean, built internally from PCF8574 + HD44780 models.
6. **Nano timing table** (ADR-005): **Proposal**: start with the table in ADR-005 (brief values + marked estimates) and calibrate in M1 — if you have a real Nano, flash `tools/nano-timing-bench/nano-timing-bench.ino` and send me its Serial output; otherwise I cross-check against avr8js with an avr-gcc build used only as a reference.
7. **Firmware ABI v1** (ADR-001): OK to add `analog_reference`, `serial_begin/end/peek/available_for_write/flush`, `i2c_begin/end/set_clock`, `spi_end`, `set_interrupts_enabled`, `fatal` and `crash_info` to the brief's list, and to bootstrap static constructors through the `ARDUSIM_HOST_API` environment variable?
8. **Project format v1** (ADR-008): OK with the schema draft (coordinates in 0.01 inch, pin-to-pin wires, defaults omitted from `properties`, examples copied to `Documents\ArduSim\Projects`)?
