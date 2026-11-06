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
powershell -ExecutionPolicy Bypass -File tools/publish/Publish.ps1 -Output publish/ArduSim   # self-contained folder, no SDK needed
```

Requires the .NET 10 SDK. The first build of any sketch (or the first `dotnet test`) downloads the pinned llvm-mingw (182 MiB) into `%LOCALAPPDATA%\ArduSim\toolchain`; that takes a few minutes once. No Arduino IDE, no arduino-cli.

Runner options: `--project <folder>` (required), `--run-for <ms>`, `--speed <x|max>` (default max), `--dump-serial <file>`, `--dump-display <file.png>` (displays at the end; several → `<file>-<id>.png`), `--dump-trace <file>` (the logic analyzer's recording as text), `--dump-circuit <file>` (at the end, every pin's voltage and current and every wire's current), `--seed <n>`, `--input <ms>:<part>:<name>=<value>` (repeatable; names are the parts' inputs: `pressed`, `value`, `turn` (encoder detents), `k0`-`k15` (keypad), `b0`-`b20` (IR remote), `x`/`y` (joystick), `temperature`, `humidity`, `distance`), `--quiet`, `--build-dir <folder>`, `--no-network` (ESP boards: Wi-Fi simulated, real connections fail). Output lines are `[   1000.008 ms] D13 LOW`, `[ ... ms] led1 brightness=1`, `[ ... ms] Serial: text`, `[ ... ms] lcd1: "row 0" "row 1"` (display text 20 ms after a change; CGRAM glyphs as `\0`-`\7`), `[ ... ms] oled1: 1024 px on`, `[ ... ms] seg1: "12.34"` (seven-segment, TM1637), `[ ... ms] mx1: 40 px on` (MAX7219, grayscale OLED), `[ ... ms] tft1: 76800 px lit` (TFT: pixels not black), `[ ... ms] lcd1: 280 px dark` (Nokia 5110), `[ ... ms] strip1: #RRGGBB ...` (WS2812, up to 16 LEDs; more: `N of M lit`), `[ ... ms] warning: ...`; part states such as `servo1 angle=90` are left out with `--quiet`. Exit codes are in `src/ArduSim.Runner/ProcessExit.cs`. `--ipc ...` is the mode the app uses (PipeSimulationSession passes the arguments).

## Repository map

```
ArduSim.sln, Directory.Build.props, Directory.Packages.props, global.json, .editorconfig
src/ArduSim.Core       engine: Timing, Circuits (nets, the nodal DC solver), Components (+ Parts), Simulation (engine, Mcu, BoardSimulation, UART, EEPROM, interrupts, WifiRadio, KeyValueStore), Diagnostics, Boards (profile types), Projects (file, template), Editing (SceneModel, SceneEditor, WireRouter, WireChannel), Buses (I2cBus, SpiBus, OneWireSlave, InfraredLink, LedStripBus), Tracing (SignalRecorder, TraceStore, BusWaveform, BusDecoders), Displays (HD44780 + A00/A02 fonts, PCF8574, OledController (SSD1306/SH1106/SSD1309), Ssd1327Controller, TftController + TftPanel, Pcd8544Controller, frames, DisplayImage), Storage (FatImage: the SD card's FAT volume), Screens (ScreenModel, SketchScanner, BodyWalker, ScreenCodeAnalyzer, ScreenCodeEdits, ScreenHardware, ScreenCodeGenerator + .Runtime, ScreenEditor, ScreenExpression, ScreenFont, ScreenCheck)
src/ArduSim.Boards     board profiles and timing tables (Nano, Uno, Mega 2560, Pro Mini 5V and 3.3V, ESP32 DevKit v1, NodeMCU v3, Wemos D1 mini)
src/ArduSim.Build      Toolchains, Sketches (preprocessor), Libraries (resolver, catalog and its client, installer, inventory), Compilation (builder, object cache, diagnostics, FirmwareSymbols), Import (PlatformIoProject)
src/ArduSim.Ipc        Messages (MessagePack), IpcChannel, ISimulationSession, PipeSimulationSession, CrashBlock
src/ArduSim.Runner     Interop (SimHostApi mirror, callbacks, loader), SimulationHost (+ restart), Network (NetworkService: real sockets for ESP Wi-Fi), ProjectFolderFiles (SD card folders), Session (SessionServer, PipeServer, InProcessSimulationSession), Headless (+ PngWriter)
src/ArduSim.App        WPF UI: MainWindow (+ Shell: toolbar stages, Рядом, explorer glue, dropped folders), Views (SchematicCanvas, PinLabels, CodeEditor + CodeEditorAssist, LcdScreen, OledScreen, TftScreen, LedScreens, PlotterView, TimelineView, ExampleThumbnails, ScreensView, ScreenMapView, ScreenFaceView, ScreenPreview, GlyphView, UnsavedDialog, SettingsDialog, LibraryDialog, AccuracySheet, ShortcutSheet, converters), ViewModels (Main + Shell, Explorer, Start, Screens; Scene, Palette, PartSearch, Properties, CodeDocument, DiagnosticHints, Plotter, Timeline, Screens), Services (build, environment, UserState, Theme, TonePlayer, ClangdClient, ExampleCatalog, LibraryService, FileKinds), Themes (Dark, Light, Controls, Parts), Localization (Strings, LogText), Resources/Strings.resx
firmware/shim          C++ Arduino shim (ABI 2.3): shared include/ (Arduino.h, wiring_private.h, sim_abi.h, sim/ incl. timers.h, led_strip.h) and src/; cores/avr, cores/esp32, cores/esp8266 and cores/esp-common (shared by both ESP cores: WiFiNetwork, HTTPClient, EEPROM); core libraries (AVR EEPROM; ESP32 WiFi, Preferences, ESP32Servo; ESP8266WiFi); variants/nano (also Pro Mini), uno, mega, esp32-devkit, esp8266; shared libraries Wire, SPI, Servo and SD (the Arduino SD API on SdFat); test/ (harness)
libraries/             vendored Arduino libraries (LiquidCrystal, LiquidCrystal_I2C, Adafruit_GFX, Adafruit_BusIO, Adafruit_SSD1306, Adafruit_SH110X, U8g2, GyverOLED, Keypad, DHT_sensor_library, Adafruit_Unified_Sensor, OneWire, DallasTemperature, IRremote, Adafruit_NeoPixel, FastLED, SevSeg, TM1637, LedControl, MD_MAX72XX, MD_Parola, RTClib, SdFat, Adafruit_ST7735_and_ST7789_Library, Adafruit_ILI9341, Adafruit_PCD8544_Nokia_5110_LCD_library, Adafruit_SSD1327), unmodified; libraries/compat/<library>/<path> replaces a vendored header or source at build time through a clang VFS overlay (U8g2: the ESP8266 font read; OneWire: Arduino pin calls; IRremote: the timer; NeoPixel, FastLED: the LED frame call; SdFat: open flags, a pointer print; Adafruit_BusIO: no fast pin I/O); library detection reads the replacement's includes
examples/              user-facing example projects (Blink, Button, FloatingInput, Potentiometer, ScreensMenu, ScreensKitchenTimer, LcdHello, LcdI2c, LcdCustomChars, OledAdafruit, OledU8g2, OledGyver, Sh1106, Esp32Blink, Esp32Fade, Esp32Tasks, Esp32WiFi, Esp32Preferences, Esp32Oled, Esp8266Blink, Esp8266Fade, Esp8266WiFi, Esp8266Eeprom, Esp8266Oled, RotaryEncoder, Keypad4x4, Joystick, Dht22, Ds18b20, Ultrasonic, IrRemote, ServoKnob, Relay, SevenSegment, Tm1637, MatrixParola, MatrixLedControl, NeoPixelRing, FastLedMatrix, Ds3231Clock, SdCardLog, TftIli9341, TftSt7735, TftSt7789Esp32, Nokia5110, OledSsd1309, OledSsd1327Esp32, LcdA02, UnoBlink, Mega2560Ports, ProMini3v3, BuzzerMelody, LogicAnalyzerDemo, LedCurrents); copied next to the app
tests/                 ArduSim.Core.Tests, ArduSim.Build.Tests, ArduSim.App.Tests (view models, no window), ArduSim.Integration.Tests (+ Projects/ fixture sketches; PerformanceTests run alone)
tools/                 toolchain-eval (ADR-002), nano-timing-bench (ADR-005 calibration), publish (self-contained folder)
docs/                  BRIEF (spec), ARCHITECTURE, UI, UI-BACKLOG, PLAN, DECISIONS, CHANGELOG, adr/
```

`firmware/` and `libraries/` are copied next to every executable that builds firmware (content items in `ArduSim.Build.csproj`).

## Conventions

- Parts that carry current are circuit elements (`Circuit.AddResistor`, `LedRule.Add`; `SetDivider` for a track), not a driver a watcher reprograms; a part reads an element's current in `Refresh`, which the net watchers call (ADR-017).
- C#: nullable on, file-scoped namespaces, records for messages, analyzers at `latest-recommended`, `TreatWarningsAsErrors` in Core, Build, Ipc. No logic in code-behind beyond view glue. Pass `CultureInfo.InvariantCulture` for machine-readable text (the owner's locale is ru-RU: decimal comma).
- Static readonly fields are initialized in textual order: declare what an initializer uses above it.
- Project boundaries are enforced by `tests/ArduSim.Core.Tests/ProjectBoundaryTests.cs`; update its allow-list only together with `docs/ARCHITECTURE.md`.
- UI strings only in `src/ArduSim.App/Resources/Strings.resx` (Russian), used via `{l:Loc Key}` / `Strings.Get`. Simulation log messages: Core reports interpolated strings with a code (and a variant when the code is shared); add the Russian pattern as `Log_<code>[.<variant>]`.
- Scene edits go through `SceneEditor` (Core, unit-tested); the canvas only translates gestures. Part art: one `Part.<type>` template in `Themes/Parts.xaml`; pin names only through `v:PinLabels` (never typed into a TextBlock), text with `Style="{StaticResource Art.Text}"`, a display's image with `Art.Screen`; `PartArtTests` checks a template against the part's definition. Ask `SceneEditor.Definition(part)` for a part's size and pins (they can depend on its properties), not the registry.
- Bus devices implement `II2cDevice` / `ISpiDevice` and register on `ComponentContext.I2c` / `.Spi` in `OnAttach`; displays expose `SimComponent.Display` and call `NotifyDisplayChanged()`. Parts with timed protocols schedule their own net changes on `Context.Engine.Clock`; 1-Wire devices derive from `Buses.OneWireSlave`; sensor readings are `PropertyDefinition.Live` (an input of the same name while running, ADR-011). LED strips listen on `ComponentContext.LedStrips`; parts with files use `ComponentContext.Files` and write back in `OnRunEnding()` (ADR-012). Hardware bus code reports its bytes to `Mcu.Recorder` for the logic analyzer (ADR-014).
- Screens (ADR-019): a project from code is edited only through `ScreenCodeEdits` (sets of edits applied at once, refused if the text moved) and `IScreensHost`; a generated project through `ScreenEditor`. Generated code must build without warnings, and what the constructor writes into a sketch must compile; the integration tests build and run both. A piece of the generated runtime goes in only if `Need` asked for it (`ScreenCodeGenerator.Runtime`): an unused static function is a warning. What a generated project can do is general: no template of any one device, in the program or in the docs. Positions in code that are not plain numbers are `ScreenElement.XCode` and so on; `XSpan` is only what a drag may replace (ADR-019), and nothing in a function with several callers is edited.
- Wires (ADR-020): `WireModel.Waypoints` holds the route's corners whoever chose them; `IsAuto` says they are the editor's and are not saved; a person's are saved with `byHand`, and corners without it are dropped on opening. An edit that changes where a part or pin is calls `RerouteAround`; every finished edit ends in `Settle` (a drag in `SettleWires`), so the layout does not depend on history.
- Colors: XAML uses `DynamicResource` tokens (both themes have the same keys); a color picked in code follows `Services.Theme` (`IsLight`, `Changed`).
- NuGet licenses: MIT, BSD, Apache or Microsoft only; list every dependency in `THIRD_PARTY.md`. Versions only in `Directory.Packages.props`.
- C++ shim: C++17, warning-clean under clang `-Wall -Wextra -Werror` (the harness tests enforce it for the AVR, ESP32 and ESP8266 cores), one `.cpp` per module, `<windows.h>` only in `src/host_bootstrap.cpp` (it also wraps threads and events for the ESP32 tasks). Board-family code goes to `cores/<core>`; shared libraries branch on `ARDUINO_ARCH_ESP32`. ABI changes: `sim_abi.h` and `src/ArduSim.Runner/Interop/SimHostApi.cs` together, same field order.
- Host callbacks never let an exception reach native code (each one catches and ends the run with an internal-error report).
- Vendored libraries are never patched silently; fixes go to `libraries/compat/`.
- Conventional commits on `main`, with no AI attribution in messages or PR descriptions (the owner's rule).

## Known limitations (current state: M12, the DC solver, done; the next milestone is to be agreed with the owner)

- Screens, generated projects (ADR-019): Cyrillic in capitals only; at most 8 programmable characters at once on a character display (the tab warns; past that the nearest ASCII shows); what a hook prints is not counted in that; `uiFlush` sends 4 cells a pass, so a whole new 2004 screen takes about 0.1 s of virtual time; a screen's id is what hook names use and cannot be changed; a value's picture shows the starting value, or a fixed time and date; actions have no `if` of their own (a transition's `Только если` is the way); free SRAM cannot be shown, and what a generated project takes on real hardware is unknown (no AVR compiler).

- Screens (ADR-019): recognition is a heuristic, and an unusual way of keeping state shows fewer transitions or none; one display per project; a project from code gets screens and transitions only if it has an enum of screens, a switch that draws and a function that handles keys, transitions there are keys, it gets no new menus, and it is not converted into a generated one; the preview's glyphs on an OLED are the HD44780 ROM's, so positions are right and shapes approximate; a value's width in the preview is a guess, so what is centered around it is only near the center, and a loop of `write` shows as one value; what a helper with several callers prints, after a cursor it sets itself, cannot be moved from here; a display class's own cells show as blocks and its Cyrillic in the ROM's shapes, not the sketch's font; items of a list in one string cannot be edited here; no custom LCD characters or bitmaps; U8g2 in page mode is refused for generated screens; `ui_screens.*` are regenerated by the app before a build, not by the Runner; the map's and the screen's mouse gestures are not automated (the view-model calls they make are).

- Wires (ADR-020): the router does not move parts or avoid crossings at any price; a wire can shift when another is added; corners set by hand are lost when one end moves alone; wire gestures are not automated.

- Explorer: what a file holds needs clangd (the toolchain), and comes a moment after the file opens.

- AVR free SRAM: `__heap_start` and `__brkval` link, and the build warns; the number worked out from them is not real.

- Libraries (ADR-018): a project's `libs` folder needs no setting. Everything from the catalog is compiled into the firmware and runs natively, with no sandbox; only the catalog's newest release of a dependency is taken and its version constraints are ignored; a library from a ZIP in a folder named like one in `libraries/compat` gets those replacement files whatever its version; folders cannot be renamed or deleted from the explorer.

- Electrical (ADR-017): DC only, no capacitance or inductance. Modules, servos, relays, buzzers and displays neither load their pins nor draw from their supplies; seven-segment displays are logic-level; the rails have no limit (a 5 V pin gives whatever is asked); an LED warns but does not burn out; PWM phases count as independent, though pins on one timer are in phase; more than 4 PWM or tone sources in one coupled island count beyond the fourth as their average voltage. Currents on wires that close a loop are not determined and not shown.

- Editor: clangd answers with the file's words until it has read the headers; go to definition can open library headers as ordinary tabs. Gestures on wires, the Рядом divider and dropped folders are not automated. The usability audit's remaining items, with their fixes, are in `docs/UI-BACKLOG.md`.

- Tools: hardware bus waveforms are drawn from the bytes (SPI as mode 0, no clock stretching); recording runs only while the Диаграмма tab is open; its rows are not saved; `analogWrite` on a buzzer is silent, and a tone shorter than a snapshot can be missed. Boards: the Uno's and Mega's duplicate SDA/SCL headers are not drawn; Mega timing is the Nano table. PlatformIO: `lib_deps` are not downloaded. The published folder was smoke-tested on the dev machine only.

- Displays: no touch controllers, TFT_eSPI or parallel TFT shields; TFT RAMRD and the read dummy clock are not modelled (reads are byte-aligned); TFT gamma/power/frame-rate commands have no effect; GRAM starts black; the Nokia 5110 contrast curve is an approximation; large TFTs on an ESP32 at 40 MHz draw slower than real time (one host call per SPI byte).

- Outputs: WS2812 only through Adafruit_NeoPixel or FastLED (a sketch's own bit-banging does not reach the strip); APA102 and other SPI LED chipsets warn; TM1637 keys warn; MAX7219 code-B decoding warns; DS3231 has only the 1 Hz square wave, no 32 kHz output, and restarts from its start time every run; the SD card is SDSC 64 MB FAT16 on hardware SPI only, synced to its folder only when a run ends normally; a servo jumps to its angle (no speed). Gestures on the new parts are not automated.
- Sensors: sending IR is not simulated (no IR LED part); ESP32 `timerBegin` and ESP8266 Ticker do not use the ABI 2.2 timers yet; a DS18B20's EEPROM lasts one run. Gestures on the new parts are not automated (the view-model calls they make are, in the offscreen scenario).

- ESP8266: no Ticker, ESP8266WebServer or RTC user memory; `Serial.swap()` warns; the software watchdog only warns; deep sleep wakes without the GPIO 16–RST wire.

- ESP32: FreeRTOS tasks are cooperative (no preemption, core number ignored); Wi-Fi networks and the real-network switch are edited in `project.simproj.json` only (`settings.wifi`, `settings.network`); hardware timers, touch, the temperature sensor, deep-sleep wake-ups other than the timer, client TLS certificates are not simulated; every ESP.restart() loads another copy of firmware.dll and parks a thread.

- The brief §7 acceptance pass on a clean Windows VM has not been done (everything was checked on the dev machine only).
- Параметры holds the theme, the canvas hint switch, currents on the schematic and the wheel's meaning.
- Canvas mouse gestures and palette drag and drop have no automated checks; the automated checks drive the view models and render the window offscreen. Canvas fps and App memory are measured by offscreen scenario runs, not in `dotnet test`.
- I2C and SPI are master-only; I2C is hardware-only (no bit-banged I2C, no slave mode). SPI modes 1 and 2 warn (device models sample on the rising edge).
- HD44780: no 5x10 font (drawn as 5x8). LiquidCrystal_I2C has no license of its own (bundled on the owner's decision; see THIRD_PARTY.md).
- Headless runs print only the shim's one-line crash notice and exit with the NTSTATUS; the symbolized report is the app's.
- In-process sessions (`--inproc`) cannot unwind a stopped run: its thread stays parked; a sketch crash takes the app down.
- The App is checked by rendering it offscreen (RenderTargetBitmap); there are no UI automation tests.
- Timer, ADC and USART registers are not simulated (warning on first write, read 0); direct port access costs 4 µs per bit.
- The Nano timing table v1 is partly estimated; calibration against real hardware or avr8js is still open (ADR-005).
- Toolchain measurements were taken on the dev machine with a scrubbed PATH, not on a clean VM.
- `tools/nano-timing-bench` is only syntax-checked with clang against a stub `Arduino.h`; it has not been compiled for AVR (no avr-gcc on this machine).
