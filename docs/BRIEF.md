# Arduino Firmware Simulator — Brief for Claude Code

Nov 2, 2026 · @Aleksey

## 1. How to work with me

This brief is the full specification for ArduSim, a desktop simulator for Arduino sketches. Read it end to end before writing code; the decisions in §3 are final, everything else is yours to decide.

- Talk to me in Russian. Code, identifiers, comments, commit messages and all docs are in English.
- I own the product, you own the engineering. Implement §3 as written and record each decision as an ADR in `docs/adr/`; do not re-argue them.
- For everything else pick a sensible default, write one line in `docs/DECISIONS.md`, and move on. Ask me only at forks that are expensive to undo (file formats, the firmware ABI, the IPC protocol, the UI framework), and batch your questions.
- Work milestone by milestone (§6). A milestone is done when: a clean clone builds, `dotnet test` is green, the app runs, `docs/CHANGELOG.md` has an entry, and you give me a two-line "how to try it".
- Before starting M3 (the UI), write `docs/UI.md`: layout wireframe (ASCII is fine), visual language, interaction rules. Wait for my OK on it.
- Keep `CLAUDE.md` current: build/run/test commands, repo map, conventions, known limitations.
- Never fake behaviour. A stubbed API logs a visible warning; an unsupported feature fails at compile time with a clear message. Loud failures beat silent wrong results.
- No scope creep: nothing from §4 "Out of scope" without asking me first.

## 2. What we are building

ArduSim (working name) is a Windows desktop app in C#/.NET that compiles and runs Arduino sketches (`.ino`) without Arduino IDE and shows the result on a visual schematic: the board, wired components, and live displays.

- Primary user: me, hobby use on my own PC. No distribution requirements beyond "clone, build, run"; a self-contained publish is nice-to-have.
- Boards: Arduino Nano (ATmega328P) first. ESP32 and ESP8266 after the MVP.
- The feature that matters most is displays: HD44780 1602/2004 (parallel and I2C backpack) and OLED SSD1306/SH1106. They are simulated at controller level, so any library works, and rendered to look like the real part.
- Non-negotiables: written in C#; no dependency on Arduino IDE or arduino-cli; a schematic canvas with real wires (Wokwi/Tinkercad style); an original, polished UI rather than stock WPF controls; the sketch's own libraries compile unmodified.

## 3. Fixed architectural decisions

These are decided. Record each as an ADR and implement as written.

### 3.1 Simulation approach: API-level, host-compiled firmware

The sketch is compiled by a host C++ compiler against our own Arduino core shim into a native x64 DLL. There is no CPU emulation. The shim implements the Arduino API in C++ and forwards every hardware interaction to the simulator through a versioned C ABI of function pointers.

- Firmware DLL exports: `sim_fw_abi_version()`, `sim_fw_init(const SimHostApi*)`, `sim_fw_setup()`, `sim_fw_loop()`, `sim_fw_dispatch_interrupt(int id)`.
- `SimHostApi` (host functions the firmware calls): `pin_mode`, `digital_write`, `digital_read`, `analog_read`, `analog_write` (PWM duty), `tone`/`no_tone`, `now_us`, `delay_us`, `yield`, `serial_write`/`serial_read`/`serial_available` (per port), `i2c_start`/`i2c_write`/`i2c_read`/`i2c_stop` (master), `spi_begin`/`spi_transfer`/`spi_settings`, `eeprom_read`/`eeprom_write`, `attach_interrupt`/`detach_interrupt`, `log(level, msg)`.
- Shim scope for the Nano profile: `Arduino.h` core (pinMode, digitalWrite/Read, analogRead/Write, analogReference, millis/micros/delay/delayMicroseconds, tone/noTone, pulseIn, shiftOut/shiftIn, map/constrain/min/max/abs, random/randomSeed, bit macros, interrupts()/noInterrupts(), attachInterrupt), `String`, `Print`/`Stream`/`Printable`, `HardwareSerial` (`Serial` with a 64-byte TX ring buffer drained at baud rate; writes block when it is full, as on real hardware), `Wire` (master mode; slave mode later), `SPI`, `EEPROM` (persisted per project), `F()`/`__FlashStringHelper`, `avr/pgmspace.h` compatibility (PROGMEM empty, `pgm_read_*` plain reads, `strcpy_P`/`memcpy_P`/`strlen_P`, `PSTR`), compatibility headers for `avr/io.h`, `avr/interrupt.h`, `util/delay.h`, `avr/sleep.h`, `avr/wdt.h`, `itoa`/`ltoa`/`utoa`/`dtostrf`.
- Direct port access `DDRx`/`PORTx`/`PINx` is supported via proxy objects that route bit changes to the pin API (D0–D7 → PORTD, D8–D13 → PORTB, A0–A5 → PORTC). Timer, ADC and USART registers (`TCCRx`, `OCRx`, `ADCSRA`, …) exist as proxies that log one warning "register X is not simulated" on first write and read as 0, so the rest of the sketch keeps running.
- Predefined macros: `ARDUINO=10819`, `ARDUINO_ARCH_SIM`, `SIM_BOARD_NANO`, `F_CPU=16000000L`, `LED_BUILTIN=13`, `NUM_DIGITAL_PINS`, `A0`–`A7`, `SDA`/`SCL`, `MOSI`/`MISO`/`SCK`/`SS`. Do not define `__AVR__` or `ARDUINO_ARCH_AVR`: libraries then take their portable code paths (digitalWrite-based) instead of AVR register code. A library with no portable path fails to compile; fix it with a per-library shim in `libraries/compat/`, never by patching the vendored source silently.
- Known fidelity limits, listed in README and in an "About simulation accuracy" panel: `int` is 32-bit and `double` 64-bit (AVR: 16/32), no cycle accuracy, ISRs are not preemptive, no register-level timers.

### 3.2 Process model

Three executables: `ArduSim.App` (WPF UI), `ArduSim.Runner` (console process that loads the firmware DLL, runs the simulation core and serves IPC), and `firmware.dll` built per sketch.

- The Runner is a fresh process per run. This gives crash isolation (a null dereference in the sketch kills only the Runner; the UI shows the diagnostic and offers Rerun), a hard stop for infinite loops (kill the process), and a clean reload after rebuild (no `FreeLibrary` games).
- IPC: named pipe, length-prefixed binary messages (MessagePack-CSharp or MemoryPack, decide in ADR-003). UI → Runner: commands (load, start, pause, resume, stop, step, set speed) and input events (button down/up, pot value, serial input, pin override). Runner → UI: state snapshots at ≤ 60 Hz as deltas (pin levels and PWM duty, display framebuffers, component states, virtual time) plus events (serial output, log and warnings, crash report, build stats). Contracts live in `ArduSim.Ipc` and are versioned.
- The UI talks to the simulation only through `ISimulationSession`. It has an in-process implementation as well (`--inproc` dev switch) so the whole stack can be stepped in one debugger. Out-of-process is the default.

### 3.3 Virtual time

Cooperative, single-threaded, deterministic: the firmware runs on the Runner's simulation thread and the simulation advances only inside host API calls.

- Every `SimHostApi` call charges a virtual cost from a per-board timing table. Nano defaults: `digitalWrite` and `digitalRead` ≈ 4 µs, `analogRead` ≈ 112 µs, I2C byte = 9 bits at the Wire clock (100 kHz default, `Wire.setClock` respected), SPI byte = 8 bits at the SPI clock, Serial byte = 10 bits at the baud rate, `millis`/`micros` ≈ 1 µs. This makes display refresh rates realistic: a full SSD1306 frame over I2C at 400 kHz takes about 23 ms.
- `delay(n)` advances n ms in slices (100 µs), running scheduled peripheral events each slice. Returning from `loop()` and `yield()` are scheduling points too.
- Pacing: 1x paces virtual time to the wall clock; 0.1x, 0.5x, 2x, 10x and "max" are available; pause; step by one `loop()` iteration or by 1/10/100 ms. Same inputs → same run.
- Scheduled events sit in a priority queue keyed on virtual time (peripheral timers, serial TX drain). PWM and tone are modeled as state (duty, frequency), not as edges.
- Interrupts: pin-change ISRs are dispatched at the next scheduling point on the firmware thread. Document that they are not preemptive.
- Runaway detection: no host call for 2 s of wall time → the Runner reports "firmware busy without API calls" and the UI offers Stop (kills the process). Native code cannot be preempted; do not try.

### 3.4 Toolchain and build pipeline

Zero IDE dependency: the app bootstraps its own portable C++ compiler.

- On first run (or from Settings) download a portable toolchain into `%LOCALAPPDATA%\ArduSim\toolchain\<version>` with a progress UI and checksum verification. ADR-002 compares portable LLVM/clang for Windows with `zig cc` (single archive, clang-based, no install) on: download size, no admin rights, gnu++17 support, straightforward x64 DLL output, reliability on clean Windows 10/11. Settings also allow pointing at an existing clang, MSVC or MinGW.
- Sketch preprocessing mirrors arduino-cli: concatenate all `.ino` files in the sketch folder (main file first, the rest alphabetically), insert `#include <Arduino.h>`, generate function prototypes, emit `#line` directives so compiler errors map back to the original file and line. `.h`/`.cpp`/`.c` files in the sketch folder compile as-is.
- Library resolution like Arduino: scan `#include` directives; search sketch folder → project library folders → `%USERPROFILE%\Documents\Arduino\libraries` if present → bundled `libraries/`. Honor `library.properties` (`src/` and legacy flat layouts, `utility/`); ignore the `architectures` field.
- Bundled libraries, vendored with pinned versions and licenses listed in `THIRD_PARTY.md`: LiquidCrystal, LiquidCrystal\_I2C (Frank de Brabander), Adafruit\_GFX, Adafruit\_BusIO, Adafruit\_SSD1306, Adafruit\_SH110X, U8g2, GyverOLED, plus our own Wire/SPI/EEPROM shims. Every bundled library has a passing example project in `examples/`.
- Compile flags match Arduino's spirit: `-std=gnu++17 -fno-exceptions -fno-rtti -Wall -O1 -g` (debug info is needed for crash diagnostics). Per-TU object cache keyed by content hash in `%LOCALAPPDATA%\ArduSim\cache`; a warm rebuild after a one-line change takes under 3 s.
- Build output panel: raw compiler output plus parsed errors (file, line, column, message), each clickable into the editor.

### 3.5 Peripherals: protocol-level simulation

Devices see signals, never library calls. A display works with any library because we emulate the controller's command set.

- Nets and pins: each component pin connects through wires to a net. Resolution per step: outputs drive HIGH/LOW; inputs read the resolved level; `INPUT_PULLUP` and external resistors to a rail give weak levels; a net with no driver and no pull is floating → reads an unstable value and raises a one-time warning "D2 is floating"; two strong drivers in conflict → warning "short circuit on net". Analog nets carry a voltage (0–5 V); `analogRead` quantizes to the board's ADC resolution. `5V`, `3V3`, `GND`, `VIN` are named rail nets; a component with unpowered VCC is inert and flagged. No SPICE, no current modeling.
- I2C bus: a net pair (SDA, SCL). The Wire shim calls `i2c_start(addr, rw)`, `i2c_write(byte)`, `i2c_read(ack)`, `i2c_stop`; the bus finds the device with that 7-bit address among devices whose SDA/SCL pins sit on those nets. No device → NACK → `Wire.endTransmission()` returns 2, as on real hardware. A bus without pull-ups still works but logs an informational note.
- SPI: `SPI.transfer` is full-duplex per byte to the device whose CS pin is LOW on the bus nets; no CS low → the byte goes nowhere and 0xFF comes back. Bit-banged (software) SPI and `shiftOut` also work because devices can sample SCK edges from the net; this bit-level path is needed for the software-SPI constructors in Adafruit libraries.
- Parallel bus (HD44780 4/8-bit): the controller samples D4–D7 or D0–D7 on the falling edge of E and reads RS/RW from their nets.
- Display controllers are state machines with unit tests driven by recorded byte streams:
  - HD44780: full instruction set (clear, home, entry mode, display/cursor/blink, shift display/cursor, function set with 4/8-bit, 1/2 lines, 5x8/5x10, set CGRAM/DDRAM address, busy flag and address counter read), 80-byte DDRAM with correct row mapping for 16x2 (0x00/0x40) and 20x4 (0x00/0x40/0x14/0x54), 8 CGRAM characters, 4-bit nibble sequencing including the init sequence, A00 font ROM (A02 later).
  - PCF8574 I2C backpack: 8-bit expander with a configurable bit map (default P0=RS, P1=RW, P2=E, P3=backlight, P4–P7=D4–D7, address 0x27; alternatives 0x3F and the other common mapping), feeding the HD44780 model.
  - SSD1306: the datasheet command set, including memory addressing mode (page/horizontal/vertical), column and page address ranges, display start line, segment remap, COM scan direction, COM pins config, multiplex ratio, contrast, invert, entire-display-on, display on/off, charge pump (accepted), basic horizontal scroll; GDDRAM 128x64 bits; I2C control byte (0x00 command, 0x40 data, Co bit) and SPI with a D/C pin; variants 128x64, 128x32, 64x48, 96x16.
  - SH1106: like SSD1306 but with 132-column RAM and a 2-column offset, page addressing only, lower/higher column address commands, no horizontal addressing mode.
- Rendering on the UI side from framebuffers: HD44780 5x8 glyphs with a visible dot grid, backlight variants (blue/white, green/black, none) and contrast; OLED pixel glow with color variants (white, blue, yellow/blue two-tone top 16 rows), contrast as brightness, display-off as dark. Pixel-perfect at any zoom (WriteableBitmap scaled nearest-neighbor).

### 3.6 Components and boards

- A component = a definition (id, name, category, pins with names and kinds, properties with editor metadata, vector art) + a behavior class (`ISimComponent`: binds to nets, reacts to net changes, schedules events) + a view (states, live values). Components register through attributes; trivial parts (resistor, LED colors) are data-driven.
- Arduino Nano v3 board model: D0–D13, A0–A7 (A6/A7 analog-only), 5V, 3V3, VIN, GND x2, RST, AREF; PWM on 3, 5, 6, 9, 10, 11; I2C on A4/A5; SPI on 11/12/13 with SS 10; UART on 0/1; external interrupts on 2/3; built-in LED on D13; 10-bit ADC; 5 V logic. Later: Uno (same MCU), ESP32 DevKit, ESP8266 NodeMCU and Wemos D1 mini (3.3 V logic, 12-bit ADC, their own pin names, more UARTs, PWM via `ledc`).

### 3.7 UI stack and shape

- WPF on the latest LTS .NET SDK installed (.NET 10 preferred, .NET 8 acceptable), MVVM with CommunityToolkit.Mvvm, DI with Microsoft.Extensions.Hosting. Editor: AvalonEdit. Docking: AvalonDock (Dirkster) or a custom split layout if docking adds too much friction, decided in ADR-004. One consistent vector icon set (Fluent UI System Icons or Lucide). `ArduSim.Core` stays UI-agnostic so an Avalonia port stays possible.
- Layout: left, a components palette (search, categories, drag to canvas); center, tabs for the schematic canvas and code files; right, properties of the selection (pins, I2C address, LED color, display size and color variant); bottom, tabbed: Serial Monitor (input line, line endings, baud, autoscroll, clear), Serial Plotter, Build output, Simulation log (warnings with "jump to component"), Pin timeline (later). Toolbar: Build, Run, Pause, Stop, Step (loop / 1 ms / 10 ms / 100 ms), speed selector, virtual time readout, board picker, status (idle, building, running, paused, crashed).
- Canvas: infinite, pan with middle drag or Space, zoom to cursor, grid and snap, place/move/rotate/flip/delete, multi-select, copy/paste, undo/redo for everything. Wires: click a pin, route with orthogonal segments and manual waypoints, color by net type (power red/black, I2C fixed colors, others selectable), hover highlights the whole net, pin tooltips with name and live level. Live state on the canvas: LED brightness from PWM duty, pressed buttons, draggable pot knobs, display content. Nice-to-have: "connect to board" one-click wiring of VCC/GND/SDA/SCL for common modules.
- Code editing: built-in tabs with C/C++ highlighting, line numbers, error markers from the build, go-to-error. The sketch folder is the source of truth: a FileSystemWatcher reloads external edits (VS Code) and warns on conflicts. Run builds first; Ctrl+B builds only.
- Visual style: our own theme, dark by default with a light variant; flat surfaces, one accent color, a consistent 8 px rhythm, readable typography (Segoe UI or Inter), subtle motion only on state changes. Interface language is Russian; all strings in `.resx` so English can be added later.

### 3.8 Project format

A project is a folder with `project.simproj.json` (schema-versioned: board, scene = components with ids, positions, rotation and properties, wires with waypoints, settings: speed, baud, library paths) and a `sketch/` subfolder with the `.ino` and companion files, or a `sketchPath` pointing at an external sketch folder. `eeprom.bin` next to the project file persists EEPROM between runs. Keep a recent-projects list; bundled examples open as copies.

## 4. Scope

The MVP is Arduino Nano with basic parts and the four display families; everything else comes after it, in the order below.

### MVP (M1–M5)

- Board: Arduino Nano.
- Components: LED (color, PWM brightness), RGB LED, pushbutton (momentary, with an optional keyboard shortcut), slide switch, potentiometer (draggable knob), resistor (pull-up/pull-down/series), piezo buzzer (shows tone frequency; audio later), LCD 1602 and 2004 (parallel 4/8-bit, or I2C backpack via an "interface" property), OLED SSD1306 128x64 and 128x32 (I2C and 4-wire SPI), OLED SH1106 128x64 (I2C and SPI).
- Buses and features: GPIO, I2C, SPI (hardware and bit-banged), UART0, PWM, ADC, EEPROM, pin-change interrupts.
- Tools: Serial Monitor, Serial Plotter, Build output, Simulation log, virtual time controls (pause, speed, step).
- All bundled libraries from §3.4 with a working example project each.
- Headless CLI in the Runner, for tests and for me: `ArduSim.Runner --project <path> --run-for 2000 --dump-display out.png --dump-serial out.txt`.

### Later (M6+), in priority order

1. ESP32 (DevKit v1) and ESP8266 (NodeMCU, Wemos D1 mini) board profiles and shims: pin maps, 3.3 V logic, `analogReadResolution`, ESP32 `ledc*` PWM, `Serial1`/`Serial2`, `ESP.restart()`/`getFreeHeap()`, `WiFi.h`/`ESP8266WiFi.h` mock (fake AP list, connect, status, localIP; real HTTP optional), `Preferences`, FreeRTOS `xTaskCreate` mapped to cooperative tasks or declared unsupported (decide then).
2. Components: rotary encoder, 4x4 keypad, DHT11/22, DS18B20 (1-Wire bus model), HC-SR04, servo (needs a Servo library shim), relay, WS2812 strip (NeoPixel/FastLED use inline asm on AVR, so a shimmed library is required), 7-segment and TM1637, MAX7219 matrix, DS3231 RTC, SD card (SPI + FAT over a host folder), joystick, IR receiver.
3. Displays: SSD1309, SSD1327 (grayscale), ST7735/ST7789, ILI9341, PCD8544 (Nokia 5110), HD44780 with the A02 font.
4. Pin timeline / logic analyzer with I2C, SPI and UART decoders; buzzer audio; Uno, Mega and Pro Mini profiles; PlatformIO project import; self-contained publish.

### Out of scope (do not build without asking)

CPU or cycle emulation; register-level timers; electrical (SPICE) simulation; scripted test scenarios; breakpoints or stepping inside sketch code; Arduino IDE or arduino-cli integration; cross-platform UI; anything multi-user or cloud.

## 5. Engineering requirements

One solution, clear project boundaries, tests that build and run real sketches.

### Repository layout

```
ArduSim.sln
src/ArduSim.Core       simulation engine: virtual clock, nets, buses, components, display controllers (no UI, no IPC)
src/ArduSim.Boards     board profiles and timing tables
src/ArduSim.Build      toolchain manager, .ino preprocessor, library resolver, compiler driver, object cache
src/ArduSim.Ipc        message contracts and named-pipe transport
src/ArduSim.Runner     console host: loads firmware.dll, hosts Core, serves IPC, headless CLI
src/ArduSim.App        WPF UI
firmware/shim          C++ Arduino core shim (Arduino.h, Wire, SPI, EEPROM, compat headers) and firmware entry points
libraries/             vendored Arduino libraries; libraries/compat for per-library fixes
examples/              Blink, Button, LCD Hello (4-bit), LCD I2C, OLED Adafruit, OLED U8g2, OLED GyverOLED, SH1106
tests/                 ArduSim.Core.Tests, ArduSim.Build.Tests, ArduSim.Integration.Tests
docs/                  ARCHITECTURE.md, UI.md, DECISIONS.md, adr/, CHANGELOG.md
CLAUDE.md  README.md  THIRD_PARTY.md
```

### Tests (xUnit)

- Core: display controllers fed recorded command streams, compared with golden framebuffers (text art for LCD, bit arrays or PNG for OLED); I2C and SPI bus behaviour (NACK on a missing address, CS gating, bit-banged SPI); net resolution (floating, pull-ups, conflicts); virtual clock and scheduler ordering; serial TX buffer timing.
- Build: preprocessor (prototype generation, `#line` mapping), library resolution order, compiler error parsing, cache invalidation.
- Integration: build and run every `examples/` project headlessly and assert on output. Blink toggles D13 every 1000 ms of virtual time; LCD HelloWorld shows "hello, world!" on row 0 and a growing counter on row 1; the Adafruit `ssd1306_128x64_i2c` example produces no NACKs and a non-empty framebuffer at frame N; U8g2 and GyverOLED examples likewise. A test fixture fetches the toolchain once and caches it.

### Robustness

- Every failure path has a UI message with a next action: toolchain missing or corrupt → re-download; compile error → jump to line; Runner crash → exception type, address, the last 20 host calls from a ring buffer, and Rerun; runaway → Stop; IPC lost → "runner exited" with its stderr.
- Autosave the scene on every Run. A crash never loses a project.
- Performance targets: canvas at 60 fps with 50 components and 200 wires; Runner sustains at least 2 million host calls per second; UI updates at most 60 Hz with deltas; working set under 300 MB.

### Conventions

- C#: nullable enabled, `TreatWarningsAsErrors` in Core, Build and Ipc, analyzers on, file-scoped namespaces, records for messages, no logic in code-behind beyond view glue.
- C++ shim: C++17, warning-clean under clang, one `.cpp` per module, a small test harness that compiles the shim standalone.
- Logging: Serilog to `%LOCALAPPDATA%\ArduSim\logs` and to the in-app log panel. Conventional commits.
- Licenses: NuGet dependencies MIT, BSD, Apache or Microsoft only. Vendored Arduino libraries keep their own licenses (LGPL, BSD, MIT are all fine as sources compiled by the user). No GPL code copied into ours; simavr and similar are references for behaviour only.

## 6. Milestones

Six milestones to the MVP; each ends in something I can run. Re-plan M6+ with me after M5.

| Milestone | Deliverable | Done when |
| --- | --- | --- |
| M0 Scaffold and plans | Solution skeleton, `CLAUDE.md`, `docs/ARCHITECTURE.md`, ADR-001 simulation approach, ADR-002 toolchain (with a measured download size and a smoke build), ADR-003 process model and IPC, ADR-004 UI stack, `docs/PLAN.md` with a task breakdown, your list of open questions | Empty solution builds; I have answered your questions |
| M1 Build and run headless | Toolchain bootstrap, `.ino` preprocessor, library resolver, shim core (GPIO, time, Serial, EEPROM), Blink compiles to `firmware.dll`, Runner runs it headless and prints pin changes and Serial output, virtual clock with pacing, tests | `ArduSim.Runner --project examples/Blink --run-for 3000` prints D13 toggles 1000 ms apart |
| M2 Simulation core and first UI | Nets, Nano board model, LED, RGB LED, button, switch, pot, resistor, buzzer, IPC, App with a fixed-layout canvas showing a hard-coded scene, Run/Pause/Stop/Step/speed, Serial Monitor | Blink blinks on screen; the Button example toggles an LED; a floating input produces a warning |
| M3 Schematic editor | `docs/UI.md` approved first; palette, placement, rotation, wires with waypoints, properties panel, undo/redo, project save/load, code editor with build errors, file watcher | I can build the Blink+button scene from scratch, save, reopen and run it |
| M4 LCD | I2C and parallel buses, HD44780 and PCF8574 models, LCD rendering, LiquidCrystal and LiquidCrystal\_I2C examples, controller unit tests with recorded streams | HelloWorld on 1602 I2C and on 2004 4-bit; `createChar` custom glyphs and `scrollDisplayLeft` work |
| M5 OLED and polish | SPI bus (hardware and bit-banged), SSD1306 and SH1106 models, OLED rendering with color variants, Adafruit, U8g2 and GyverOLED examples, Serial Plotter, Simulation log, dark/light themes, examples gallery, README | All of §7 passes; this is the MVP |
| M6+ | ESP32/ESP8266 profiles and shims, more components and displays, logic analyzer, per §4 | Planned together after M5 |

## 7. Acceptance criteria for the MVP

The MVP is accepted when all of the following pass on a clean Windows 10/11 machine with no Arduino IDE installed.

- [ ] First launch offers to download the toolchain, shows progress, verifies the checksum, and the app is usable right after.
- [ ] New project → pick Nano → drag "LCD 1602 I2C" → wire VCC, GND, SDA, SCL → paste the standard LiquidCrystal\_I2C HelloWorld sketch → Run: the text appears on the LCD within 1 s of the build finishing.
- [ ] Change the text, Run again: warm rebuild under 3 s, the new text shows.
- [ ] Same sketch on a 2004 wired in 4-bit parallel mode shows 4 lines; `createChar` glyphs render; display scroll works.
- [ ] The Adafruit `ssd1306_128x64_i2c` example runs with its animations at about real-time speed at 1x and visibly faster at 10x; no NACK warnings in the log.
- [ ] U8g2 and GyverOLED examples render correctly on SSD1306 and SH1106, over I2C and over hardware SPI; the Adafruit software-SPI constructor also works.
- [ ] A button on `INPUT_PULLUP` toggles an LED; `analogWrite` dims an LED visibly; a potentiometer changes `analogRead` values live in the Serial Plotter.
- [ ] A floating input and a short circuit each produce one clear warning pointing at the component.
- [ ] A sketch with `while(true){}` is stoppable with Stop; a sketch that dereferences a null pointer kills only the Runner, and the UI shows the diagnostic and offers Rerun.
- [ ] Serial Monitor sends input to `Serial.read()`; `Serial.print` output appears with baud-realistic timing at 1x.
- [ ] Editing the `.ino` in VS Code is picked up by the editor; the next Run uses the new code.
- [ ] Project save, close, reopen restores the scene exactly, including wire waypoints and EEPROM contents.
- [ ] `dotnet test` is green, including the integration tests that build every example.
- [ ] The UI has the dark theme by default and a light theme, Russian strings, no stock-WPF look.

## 8. Open questions for M0 and references

Answer these in M0 with a proposal each; I will confirm or redirect.

- Toolchain: portable LLVM/clang vs `zig cc`, with the measured archive size and a smoke build of a DLL on a clean machine.
- IPC serializer: MessagePack-CSharp vs MemoryPack.
- Docking: AvalonDock vs a custom split layout.
- SSD1306 scrolling commands: full implementation or the basic horizontal scroll only.
- LCD with backpack: one component with an "interface" property (parallel / I2C), as I lean, or LCD plus a separate PCF8574 part.
- Timing table values for the Nano: confirm against avr8js or measured numbers and note the source.

References, for behaviour only; never copy GPL code:

- ArduinoCore-API and arduino-cli sketch preprocessing rules (prototype generation, `#line`).
- Datasheets: HD44780U, PCF8574, SSD1306, SH1106.
- The bundled libraries' sources, for the exact command sequences they send to the displays.
- Wokwi as the UX reference for the canvas and wiring; avr8js (MIT) and simavr (GPL, read-only reference) for timing behaviour.
