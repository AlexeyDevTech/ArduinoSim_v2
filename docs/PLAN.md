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

## M4 — LCD ✅

Done when HelloWorld shows on a 1602 over I2C and on a 2004 in 4-bit mode, and `createChar` and `scrollDisplayLeft` work.

- [x] I2C bus routing in Core (address match on the SDA/SCL nets, NACK warnings that name the address and point at miswiring, pull-up note); Wire shim (32-byte buffer, repeated start, `endTransmission` codes 0/2/3, `requestFrom`); harness and Core tests.
- [x] Parallel bus sampling on the falling edge of E (with the controller's pull-ups on RS, RW, D0–D7); HD44780 model (full instruction set, 1-/2-line DDRAM maps, CGRAM, 4/8-bit with nibble sequencing, busy flag and address counter reads, A00 ROM); PCF8574 model with the standard and mjkdz bit maps; recorded-stream tests.
- [x] LCD component (16x2/20x4, I2C/4-bit/8-bit with the pins changing, address, backpack, backlight blue/green/none, contrast from V0 or the property, power check) and a dot-grid renderer; palette presets.
- [x] LiquidCrystal 1.0.7 and LiquidCrystal_I2C 1.1.2 vendored; examples LcdHello (2004, 4-bit), LcdI2c (1602), LcdCustomChars (createChar, scrollDisplayLeft); integration tests, including the libraries' own HelloWorld, Scroll and CustomCharacter examples unmodified.
- Not yet: HD44780 5x10 font mode (rendered as 5x8), the A02 ROM (later list), bit-banged (software) I2C, I2C slave mode in Wire.

## M5 — OLED and polish (MVP) ✅ on the dev machine

Done when every item of brief §7 passes. All of them pass on the development machine; the pass on a clean Windows VM is still open.

- [x] SPI bus (hardware byte path, bit-level edge path, CS gating) and SPI shim; tests.
- [x] SSD1306 (all addressing modes, remap/scan, multiplex, contrast, invert, the full scroll set per Q4, variants 128x64/128x32/64x48/96x16) and SH1106 models; golden tests against the Adafruit init streams.
- [x] OLED component (I2C or SPI, the pins following the choice; address; panel colors; power and reset) and renderer (glow, white/blue/yellow-blue two-tone, contrast, display off); palette presets.
- [x] Vendored Adafruit_GFX, Adafruit_BusIO, Adafruit_SSD1306, Adafruit_SH110X, U8g2, GyverOLED (no compat shims were needed); examples OledAdafruit, OledU8g2, OledGyver, Sh1106; integration tests check exact pixels for U8g2 and GyverOLED on SSD1306 and SH1106 over I2C and hardware SPI, the Adafruit software-SPI example, and the Adafruit I2C example with no NACKs.
- [x] Serial Plotter, light theme (Параметры), examples gallery with scene thumbnails, the "О точности симуляции" sheet, README; the Potentiometer example for the plotter and `analogWrite`.
- [x] Performance: ≥ 2 M host calls/s (4 M measured, in `dotnet test`), canvas 60 fps zooming and dragging with 50 parts/200 wires, ≤ 300 MB working set (234–278 MB) — see DECISIONS.
- Not yet: the acceptance pass on a clean Windows VM (no VM here); first-launch toolchain download on a machine that never had it was last exercised in M1; canvas mouse gestures are still checked by hand only.

## M6+ — agreed with the owner on 2026-11-02 (brief §6)

In the order of brief §4 "Later". Each milestone ends in examples that build and run in `dotnet test`, like the MVP. Carried over: the MVP pass on a clean VM, the Nano timing calibration (ADR-005).

**M6 — ESP32 DevKit v1 ✅ (2026-11-03).** Done when ESP32 versions of Blink, an `ledc` fade, a Wi-Fi scan and connect, a Preferences boot counter, two FreeRTOS tasks and an OLED example run on the canvas and headless. All six run, headless in `dotnet test` and on the canvas. Not yet: editing the Wi-Fi networks and the real-network switch in the UI (they are `settings.wifi` and `settings.network` in `project.simproj.json`); a port selector for Serial1/Serial2 in the Serial Monitor (their output is drained but shown nowhere); ESP32 SPI displays are not in the tests.
- Board profile: the 30-pin DevKit v1 pin map, 3.3 V logic, input-only GPIO 34–39, 12-bit ADC (ADC2 pins warn while Wi-Fi is on), DAC on 25/26, the on-board LED on GPIO 2, board art; a timing table of marked estimates.
- Shim (the Arduino-ESP32 core 3.x API, plus the 2.x `ledcSetup`/`ledcAttachPin` still common in tutorials): `INPUT_PULLDOWN`, `analogReadResolution`, `analogWrite`, `ledcAttach`/`ledcWrite`/`ledcWriteTone`, `dacWrite`, `Serial1`/`Serial2` with pin arguments, `Wire.begin(sda, scl)` and `Wire1`, SPI on any pins (HSPI/VSPI), `ESP.restart()`, `ESP.getFreeHeap()` and friends (fixed documented values), `esp_random()`, `esp_timer_get_time()`, `EEPROM.begin/commit`, `Preferences`, the `WiFi.h` mock, cooperative FreeRTOS tasks (see the questions).
- Predefined macros as the real core sets them (`ESP32`, `ARDUINO_ARCH_ESP32`, `ARDUINO_ESP32_DEV`, `F_CPU=240000000L`), because sketches choose `WiFi.h` with them; library code behind them that calls ESP-IDF functions gets a compat shim, never a silent patch.
- UI: the board picker switches boards (wires to pins the new board lacks are removed, in one undo step); the properties panel of the board lists its Wi-Fi networks.
- The bundled display libraries run on ESP32 pins (I2C 21/22, VSPI 18/23/5) in the tests.

**M7 — ESP8266: NodeMCU v3 and Wemos D1 mini ✅ (2026-11-03).** Done when ESP8266 versions of Blink, PWM fade, Wi-Fi connect, EEPROM commit and an OLED example run. All five run, headless in `dotnet test` and on the canvas. Not yet: `Serial.swap()` (warns), the Ticker library, ESP8266WebServer, RTC user memory, deep-sleep wake without the GPIO 16–RST wire check.
- D-pin names mapped to GPIO, A0 with the boards' 0–3.3 V divider, `analogWriteRange`/`analogWriteFreq`, Serial1 TX-only on GPIO 2, `ESP8266WiFi.h` mock, `EEPROM.begin/commit`, `ESP.*`; a warning when `loop()` blocks for seconds without `yield()` (the soft watchdog would reset a real board).

**M8 — Components I: inputs and sensors ✅ (2026-11-03).** Rotary encoder, 4x4 keypad, joystick, DHT11/DHT22, DS18B20 on a 1-Wire bus model, HC-SR04, IR receiver; the libraries people use with them (Keypad, DHT sensor library, OneWire and DallasTemperature, IRremote), each with an example. All seven examples run, headless in `dotnet test` and on the canvas; the libraries also run on the ESP32 in the tests. Not yet: sending IR (no IR LED part; `SEND_PWM_BY_TIMER` warns), ESP32 `timerBegin` and ESP8266 Ticker on the new timer interrupts, the Encoder and NewPing libraries, keyboard keys for the keypad, a DS18B20 EEPROM kept across runs, canvas gestures without automated checks (the view models they call are checked).
- Firmware ABI 2.2 (timer interrupts) and header replacement in `libraries/compat` (ADR-011).

**M9 — Components II: outputs and modules ✅ (2026-11-03).** Servo (a Servo library shim), relay, WS2812 strip (Adafruit_NeoPixel and FastLED shims: both use inline asm on AVR), 7-segment, TM1637, MAX7219 matrix, DS3231 RTC (time from the host clock, settable), SD card (SPI and FAT over a host folder in the project). All ten examples run, headless in `dotnet test` and on the canvas; the LED and SD libraries also run on the ESP32 in the tests. Not yet: APA102 and other SPI LED chipsets (warn), WS2812 bit-banged by a sketch itself, TM1637 keys, MAX7219 code-B digits, the DS3231 32 kHz output and faster square waves, an RTC time kept between runs, SD cards over bit-banged SPI, SDHC/exFAT cards, a card pulled out while running.
- Firmware ABI 2.3 (LED strip frames), timer interrupts on time inside long calls, compat replacements counted by the library detection (ADR-012).

**M10 — Displays ✅ (2026-11-03).** SSD1309, SSD1327 (grayscale), ST7735/ST7789, ILI9341, PCD8544 (Nokia 5110), the HD44780 A02 ROM; with Adafruit_ST7735/ST7789, Adafruit_ILI9341, Adafruit_PCD8544 and U8g2 examples (ADR-013). Seven examples run headless in `dotnet test` and on the canvas. Not yet: touch controllers (XPT2046), TFT_eSPI, parallel TFT shields, RAMRD pixel reads, the TFT dummy read clock, SSD1309/SSD1327 scroll with vertical offset combinations beyond the datasheet figures, a Cyrillic ROM beyond A02.

**M11 — Tools and boards ✅ (2026-11-05).** Pin timeline / logic analyzer with I2C, SPI and UART decoders; buzzer audio; Uno, Mega 2560 and Pro Mini profiles; PlatformIO project import; self-contained publish (one folder, no .NET SDK needed) (ADR-014, ADR-015). Not yet: saving the diagram's rows with the project, recording while the tab is closed, SPI modes other than 0 in hardware waveforms, sound for `analogWrite` on a buzzer, the Uno's and Mega's duplicate SDA/SCL headers, `lib_deps` downloads.

Questions answered by the owner on 2026-11-02: the order above as in the brief; firmware ABI 2.0 (a clean major version, ADR-009); cooperative FreeRTOS tasks (ADR-009); a Wi-Fi mock plus a real network through the host (ADR-010), networks in the project settings and Preferences in `preferences.json`.

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
**Usability pass ✅ (2026-11-05).** Research, audit and the owner's choices: code beside the schematic, a project explorer, the editor's IDE side through clangd, folding toolbar, start view, wiring rules, hints and F1, palette, properties, contrast and focus (ADR-016). Not yet: docs/UI-BACKLOG.md.

**M12 — Electrical DC solver ✅ (2026-11-06).** Chosen by the owner on 2026-11-05: nodal analysis of voltages and currents on every change of the circuit, LEDs with forward voltage and brightness from current, dividers, pin overload above 40 mA, an LED without a resistor, currents on the schematic. No capacitors or transients; virtual time and sketch speed stay as they are. This narrows brief §4's "no SPICE" on the owner's decision (ADR-017).

**Screens and transitions ✅ (2026-11-06).** The owner's request of 2026-11-06, outside the milestone list: the Экраны tab, screens read from a sketch's code and edited in place, generated code for a project without them (ADR-019).
