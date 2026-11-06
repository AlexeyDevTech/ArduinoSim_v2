# Changelog

## Part drawings checked against their pins (2026-11-06)

The owner found the keypad's pin names squeezed to one side, away from its pins. The names were one line of text spaced by hand, on about a dozen parts. All 54 layouts were rendered with their real pin positions over the art.

- **Pin names come from the pins.** One element, `PinLabels`, places them for every part and board: centered on each pin in a row, beside it in a column. They cannot drift, and names too wide for the pitch are squeezed instead of running together.
- Corrected with it: the Mega's double header and the Pro Mini's A4-A7, whose names lay on top of the pins; `GND.3`, `5V.3` and the like, now `GND`, `5V`; the relay's contact, drawn on NO at rest (COM is on NC until the coil pulls in); the IR receiver's names floating above it; the servo's written across its wires; `NodeMCU v3` running into the ESP module.
- **Names where there were none:** DHT, DS18B20, RGB LED, potentiometer, slide switch, buzzer.
- **Pads where wires landed on nothing:** seven-segment displays, MAX7219 and WS2812 now show rings with names. For this the MAX7219 board is 30 units wider, the WS2812 strip and matrix 20 wider, the TM1637 10 taller, and a ring has a tab for its pads. Projects open as before: wires hold to pin names.
- **Text stays readable on a flipped or turned part:** it was mirrored and upside down, keys and readings included. Text is now never mirrored and never upside down; a display's picture is not mirrored either, and turns with its part as real glass does.
- `PartArtTests` holds this together for every layout: names clear of each other and of the pins and inside the part, no pin names typed into the art, leads ending on pins, drawings the size of their parts, the relay's contact.

How to try it: open Keypad4x4 and Mega2560Ports and look at the names under the pins; select a part on any schematic and press F, then R twice. On the Relay example, the contact sits on NC until D7 energizes the coil.

## Wires that hold on, a tree of what files contain, a constructor of screens (2026-11-06)

The owner's three remarks on the last version.

**Wires** (ADR-020). They were corners in a file, and stayed behind when a part moved.

- The editor lays wires out: straight out of each pin, around parts, each net on its own track.
- A part moved, rotated or resized takes its wires with it, laid out anew at once; wires go aside for a part set down on them.
- Over a target pin the ghost shows the wire it will be. Разложить, on the canvas and in its menu, lays out all of them in one undo step. Corners set by hand stay until an end moves alone.
- A route of the editor's is not saved; the file format is the same.

**Explorer.**

- A file has a badge for what it is (`ino`, `c++`, `c`, `h`), here and on its tab.
- As in Visual Studio, a file holds what is in it: the header of its name, its classes with their members, its functions and variables. The Структура group is gone. The file on screen is open in the tree; a chevron opens any other.
- Alt+O goes between a header and its source. A new `.h` starts with `#pragma once`, a new source with the `#include` of its header.

**The Экраны tab is a constructor** (ADR-019), in a project from code as well as a generated one.

- Add text, values and shapes from the toolbar, by a click or by dropping them where they go. Edit text in place with a double-click, drag with the result drawn live, resize a shape by its handle, centre it, duplicate it with Ctrl+D.
- On the map, a double-click on empty space makes a screen, a drag from a card's handle to another card makes a transition, and the right button has the rest.
- In a project from code all of this is written straight into the source in the sketch's own style, with undo: an element where the screen's drawing ends, a constant and a `case` for a screen, `if (screen == A && key == '5') { enterScreen(B); return; }` for a transition. Keys, targets, expressions, a screen's name and the start screen can be changed; elements, transitions and whole screens can be removed where they stand alone. What the sketch's shape does not allow is refused with the reason. Tests build and run what this writes.

**Also found on the way.** A sketch with its `#include`s in headers under `src` did not build, because libraries reached only through them were not found. NanoOS_v1 is such a sketch, and it builds and runs now. `__heap_start` and `__brkval` link on AVR boards, with a build warning that free SRAM worked out from them is not real.

How to try it: open a project, drag a part around the schematic, press Разложить. Then Проект: open a `.cpp` and look under it. Then Экраны → Экран: drag Текст onto the display, double-click it.

## Screens and transitions (2026-11-06)

The owner's request: an editor of screens and transitions for projects with a 1602, 2004 or OLED display, and forming that structure from an existing project when possible (ADR-019).

- A new tab, Экраны, beside Схема. Карта shows every screen as a picture with its transitions as labelled arrows; Экран shows one large, with its text dragged by character or by pixel. The left panel lists the screens, the right panel shows what is selected, and Рядом keeps the code beside the tab.
- A project with screens in its code gets all this from the code. It finds the screen variable, the transitions with their keys and menu items, the screens' text, menus and values, through wrappers such as a project's own buffer class in front of the display. The code stays the source of truth: what was read as exactly one token changes in place, in its tab, with undo; what is computed at run time is shown and left alone; a line says why. Only map positions are stored, in `screens.json`.
- A project without screens can create them. `screens.json` is then the model, `ui_screens.h` and `ui_screens.cpp` are generated from it before each build (a `switch` per screen, no heap, no warnings), and `ui_hooks.cpp` is yours and only ever added to. The display, buttons, keypad and encoder come from the schematic. Drag from one card to another to connect them. A sketch's own display object stays its own.
- All this was built and run on the simulated hardware: a 2004 on I2C and a 1602 on parallel pins, with buttons, a keypad and an encoder; an SSD1306 and an SH1106 with Adafruit's libraries; U8g2, GyverOLED and Adafruit with a display object the sketch already had.
- New example: ScreensMenu.

How to try it: open the ScreensMenu example, then Экраны, and drag a card or, on Экран, a text; Запуск, then press the encoder's knob. Or open a project with screens in its code and look at the map.

## Libraries are added, not configured (2026-11-06)

The owner's request: the text field «Папки библиотек (через ;)» asked for paths nobody could guess, and a wrong one said nothing (ADR-018).

- The field is gone. A project's `libs` folder is always searched, without any setting. `settings.libraryPaths` in the project file still works, so PlatformIO imports and old projects build as before.
- The Библиотеки group of the Проект page lists what is in `libs` and what the last build used, with where each comes from. A click opens its header, the ✕ moves a project's library to the Recycle Bin, and Добавить… opens the new dialog.
- The dialog searches the Arduino catalog (about 10 400 libraries) by name, header or a word from the description, and installs into the project: a chosen version, with what it depends on. A download shows progress, can be cancelled and is checked against the catalog's SHA-256; the project changes only when everything is ready. The catalog is saved and fetched at most once a day; without a network the saved copy is used and says so.
- Из папки… and Из ZIP… take a library, a folder of libraries or a GitHub archive.
- A bundled library says so and is not installed over; a library for other boards says so and can still be tried.
- The compiler's "file not found" has a Найти библиотеку… button, which opens the dialog with that header searched.

How to try it: write `#include <Bounce2.h>` in a sketch and press Собрать; then Найти библиотеку… beside the error, Установить, and Собрать again.
## Project explorer as a tree (2026-11-06)

The owner's request: the Скетч group listed every file flat, with `src\` in each name.

- Folders are rows now, with a chevron, the number of files they hold and their contents indented below. A click or ← → folds one, and each project remembers what is folded (`ui-state.json`, for projects on the recent list).
- In each folder its .ino files come first, then subfolders, then the other files, so a long `src` does not push the main file out of sight.
- A file that comes to the front unfolds its folders and scrolls into view.
- Новый файл здесь… on a file or folder; a typed name may include a folder (src/app.cpp) and can no longer leave the sketch. Свернуть все папки and Развернуть все папки on a folder's menu.
- Explorer rows are 24 px instead of 28 px, to fit more of a tree.

How to try it: open a project with subfolders in its sketch (or make one: Новый файл… → src/app.cpp), Ctrl+Shift+E; click a folder to fold it.

## M12 — Electrical DC solver (2026-11-06)

Voltages and currents are now calculated, on every change of the circuit (ADR-017). Virtual time does not move, and sketches run at the same speed as before: a bare pin slightly faster, a pin with an LED toggled in a tight loop 25 % slower in real time, still 7 times faster than real time.

- Nodal analysis of what is connected: resistors, potentiometers and joysticks as real dividers, so a load pulls a wiper or a divider down as on the bench. MCU outputs have an output resistance, so a loaded pin droops.
- LEDs have a forward voltage and a series resistance, and their brightness follows their current: 220 Ω looks full, 1 kΩ dimmer, 10 kΩ or an `INPUT_PULLUP` glows faintly. Series and parallel LEDs behave: three white ones stay dark on 5 V; of a red and a blue in parallel only the red lights.
- New warnings: a pin above its limit (40 mA, ESP8266 12 mA), an LED above 30 mA, which is what no resistor looks like. A short circuit keeps its own warning.
- Currents on the schematic: LEDs and resistors show them while running (Параметры can hide this); pin and wire tooltips and the Свойства pin rows give voltage and current, a board pin says whether it gives or takes it, and PWM gives the average and the peak.
- Runner: `--dump-circuit <file>` writes every pin's voltage and current and every wire's current.
- Fixed: what was reported while the scene is built (a part the registry does not know, a bad wire, an LED across the rails) never reached the log or the headless output, because they subscribed only after it.
- Example LedCurrents: 220 Ω, 4.7 kΩ and no resistor side by side, and a divider on A0.
- Not modelled: capacitance and transients; the load of modules, servos, relays and buzzers; limits on the rails; seven-segment displays stay logic-level. О точности симуляции says so.

How to try it: open the example «Токи и светодиоды» and press Запуск: compare the two LEDs and the milliamps above them, hover over a wire, hold the button to overload D6.
Headless: `src/ArduSim.Runner/bin/Debug/net10.0/ArduSim.Runner.exe --project examples/LedCurrents --run-for 1200 --input 600:btn1:pressed=1 --dump-circuit circuit.txt`
## Usability pass (2026-11-05)

An audit of the interface against Wokwi, Tinkercad, SimulIDE, Arduino IDE 2, VS Code and the Windows guidelines (ADR-016).

- Fixed: Собрать and Запуск stayed grey by mouse after the first project of a session. A click on a running part now selects it, so a sensor's slider can be reached, and a plain click no longer warns. The keyboard focus ring can now be seen on the dark theme. Three examples had corners with their numbers missing (Relay, SdCardLog, ServoKnob).
- Code beside the schematic: Рядом (Ctrl+\); F7 flips between them; Run returns to the schematic.
- A project explorer as a page of the left panel: sketch files with new, rename and delete; the file's functions; the parts; the libraries of the last build.
- The editor: find and replace, go to line, comment, format, moving lines, self-closing brackets, a right-click menu. clangd from the toolchain gives completion, signatures, hover and go to definition. Compiler messages get a line of plain Russian and, where clang names one, an Исправить button. Marks follow the text, and columns are right on Cyrillic lines.
- The toolbar folds by stages on a narrow window, and nothing is cut off any more; the window can be 960 × 560 and reopens where it was. On a low window the bottom panel opens only when there is output. While running: Перезапуск in place of Запуск, a dot when the code is newer than the run, levels on pins and wires, a status bar above the center.
- Every disabled control says why; every tooltip has the key. Arduino IDE and VS Code keys work as well (Ctrl+R, Ctrl+U, Ctrl+Shift+M, F12). F1 opens every key and gesture, with a filter. One line of hint on the schematic, chosen by what is going on.
- The start view: a name and one tile per board make a new project; the gallery has search, a family filter, topics, Russian titles and descriptions. An example says what to try. A plain Arduino sketch folder opens as a project, and a folder can be dropped on the window.
- Wires: drag from pin to pin; the right button cancels; the nearest pin wins; ends can move to another pin; corners can go; Сбросить маршрут; a new wire takes the bend that does not run along other pins; crossings show a gap.
- Palette: most-needed parts first, counts, folding remembered, tooltips with the pins, a search that understands «датчик», «экран», «мотор», the wrong keyboard layout and typos; A, ↑↓, Enter and Esc from the keyboard; a part never lands on top of another.
- Properties: edits keep the focus; ↑↓, the wheel and Esc on numbers; 10k and 4,7к are understood; levels in the pin list and a click to light a net; common properties for a multi-selection. The fake baud list is gone.
- Bottom tabs: Журнал has its filter and its ⚠ on the part opens the message; Монитор has Время and says why it cannot send; Сборка shows counts; Диаграмма scrolls its rows; empty tabs say what will appear. A crash stays in Журнал after its banner closes; a sketch that comes back clears «Не отвечает».
- The compiler download has a progress bar and Отмена, and the window keeps answering.
- The owner's additions: pins that land on each other connect; right-drag and arrows pan, with a touchpad setting; signal wires differ in color by board pin; inputs from the keyboard and latching by Ctrl+click; Стоп cancels a build and the status counts it; Сохранить как…; a file changed on both sides is never written over; Очистить EEPROM; the Diagram stays live while zooming, with Вписать всё and К последним, and works from the keyboard; the tabs' tools sit on their row; a Plotter series hides by its legend; the bottom panel folds under 760 px and Ctrl+Shift+\ hides all panels; Ctrl+F6 between panes and Alt for the menu; Windows text size; 20 px run icons; banners fade in.
- The Diagram is right on every run, opens at a readable scale, marks time it did not record and labels its ruler properly. The Monitor appends instead of rewriting, and Cyrillic survives chunk boundaries.
- A failure nothing expected shows a banner and keeps the work open. A Windows-1251 sketch is read correctly instead of being damaged by the first save. Typing a block, completion and Replace no longer damage what was typed.
- Left for later, with fixes written out: `docs/UI-BACKLOG.md`.

## M11 — Tools and boards (2026-11-05)

- Диаграмма tab: a logic analyzer with signal rows, a time ruler and I2C, SPI and UART decoders, live during a run. Hardware `Wire`, `SPI` and `Serial` traffic shows with its waveforms; bit-banged traffic decodes from real edges on any pins. Wheel zooms, a drag pans, a click sets the marker for Δ; a click on a wire on the running schematic adds its pins. PWM and tones show as bands with their duty or frequency.
- Buzzer audio with a speaker button in the toolbar: on by default, remembered.
- The Serial Monitor has a port list on boards with several UARTs; `Serial1`-`Serial3` of the Mega and `Serial1`/`Serial2` of the ESP boards can now be seen and written to.
- New boards: Arduino Uno R3, Mega 2560 (ports A-L, four UARTs) and Pro Mini (5 V at 16 MHz, 3.3 V at 8 MHz).
- PlatformIO projects open in place: «Открыть папку…» on a folder with `platformio.ini` takes the board, sources, flags, include and library folders. Plain C++ projects without `.ino` build.
- `tools/publish/Publish.ps1`: a self-contained folder that needs no .NET SDK.
- Headless runs: `--dump-trace <file>`; each port's lines are kept apart. IPC protocol 2.
- Examples UnoBlink, Mega2560Ports, ProMini3v3, BuzzerMelody, LogicAnalyzerDemo.
## M10 — Displays (2026-11-03)

- New parts: color TFTs ST7735 (1.8" black and green tab, 1.44", 0.96"), ST7789 IPS (1.3" without CS, 2.0", 1.14") and ILI9341 2.8"; Nokia 5110 (PCD8544); the OLED part gains the SSD1309 (2.42") and the grayscale SSD1327 (1.5" 128x128); the LCD part gains the A02 character ROM (European, with Cyrillic capitals).
- TFT controllers on SPI with MADCTL rotations, 12/16/18-bit pixels, windows, vertical scrolling, inversion and the status reads; each module's glass sits on GRAM as on the real board, so a wrong tab, rotation or missing INVON looks as it would on hardware. The backlight pin dims the glass.
- Bundled Adafruit ST7735 and ST7789 1.12.0, Adafruit ILI9341 1.7.0, Adafruit PCD8544 2.0.3, Adafruit SSD1327 1.0.4; U8g2 drives the SSD1309 and SSD1327 too. The libraries build on the Nano, the ESP32 and the ESP8266.
- Headless runs print TFT, graphic LCD and grayscale OLED pixel counts; `--dump-display` writes color PNGs.
- Hardware SPI bytes no longer allocate (faster TFT fills).
- Examples TftIli9341, TftSt7735, TftSt7789Esp32, Nokia5110, OledSsd1309, OledSsd1327Esp32, LcdA02.
## M9 — Outputs and modules (2026-11-03)

- New parts: SG90 servo, 5 V relay module, seven-segment display (1 or 4 digits, common cathode or anode), TM1637 4-digit module, MAX7219 8x8 matrix (one module or the FC-16 chain of four), WS2812B strip, ring and 8x8 matrix, DS3231 RTC module, microSD module; a "Модули" palette group.
- Servo library in the shim (Arduino API, pulses from a timer interrupt) and ESP32Servo.h; the servo horn follows the pulse width.
- Firmware ABI 2.3: one call per WS2812 frame, timed as on the wire; frames go through DOUT to chained strips. Adafruit_NeoPixel and FastLED work on every board through `libraries/compat`.
- Seven-segment digits glow with their multiplexing duty; TM1637 and MAX7219 follow their datasheets and MD_MAX72XX's module wiring; displays print their digits or lit pixels in headless runs.
- DS3231: time from the computer or a fixed start, alarms on SQW/INT, temperature; RTClib works unchanged.
- SD card: the SD library's API (the shim's own, on SdFat) on a simulated 64 MB card in SPI mode; its files are the project's `sd/` folder, read at Run and written back at Stop; a Properties button opens the folder.
- Interrupts from timers run at their time even inside long calls (analogRead).
- Bundled libraries Adafruit_NeoPixel 1.15.5, FastLED 3.9.20, SevSeg 3.7.2, TM1637 1.2.0, LedControl 1.0.6, MD_MAX72XX 3.5.1, MD_Parola 3.7.7, RTClib 2.1.4, SdFat 2.3.1. `libraries/compat` replacements now also count for library detection.
- Examples ServoKnob, Relay, SevenSegment, Tm1637, MatrixParola, MatrixLedControl, NeoPixelRing, FastLedMatrix, Ds3231Clock, SdCardLog; the LED and SD libraries also run on the ESP32 in the tests.
## M8 — Inputs and sensors (2026-11-03)

- New parts: KY-040 rotary encoder, 4x4 membrane keypad, analog joystick, DHT22 and DHT11, DS18B20, HC-SR04, IR receiver and the 21-key IR remote, with their drawings; a "Датчики" palette group.
- Sensors answer in virtual time with the datasheet timings, so the libraries' own bit-banging runs unchanged: DHT start signal and 40-bit answer, the HC-SR04 echo (58.3 µs per cm, no echo beyond 4 m), NEC frames and repeats on the IR receiver.
- 1-Wire bus model (ADR-011): reset and presence, read and write slots, wired-AND between devices, search and alarm search; the DS18B20 with its scratchpad, 9–12 bit conversions and their times, the 85 °C power-on value, EEPROM copy and parasite power. Warnings for a 1-Wire bus without a pull-up, a DHT start signal too short for its model, a short TRIG pulse, sensors without power.
- Sensor readings stay live while running: in Properties (slider and field) and by dragging the part up and down (Shift: humidity) or the wheel; they are not saved. Encoder: wheel or drag to turn, click to press. Joystick: drag the cap, click to press. Keypad and remote: click and hold the keys.
- Firmware ABI 2.2: periodic timer interrupts; `delay()` stops at scheduled events so timer ticks run on time.
- Bundled libraries Keypad 3.1.1, DHT sensor library 1.4.7 with Adafruit Unified Sensor 1.1.15, OneWire 2.3.8, DallasTemperature 4.0.6 and IRremote 4.7.1. `libraries/compat` now replaces headers too (a clang VFS overlay): OneWire uses the Arduino pin calls on every board, IRremote takes its 50 µs tick from a simulated timer.
- Examples RotaryEncoder, Keypad4x4, Joystick, Dht22, Ds18b20 (two sensors on one bus), Ultrasonic and IrRemote; the libraries also run on the ESP32 in the tests.
## M7 — ESP8266: NodeMCU v3 and Wemos D1 mini (2026-11-03)

- Boards NodeMCU v3 and Wemos D1 mini (ESP-12E/F, 80 MHz, 3.3 V): D0–D8, A0, RX, TX with GPIO numbers; GPIO 16 has a pull-down and no pull-up or interrupt; A0 reads 0–3.3 V as 0–1023 and any other pin reads 0 or 1023, as the core does; the module's blue LED on GPIO 2 lights while the pin is LOW; board drawings with a Wi-Fi indicator.
- ESP8266 core (Arduino core 3.x API): software PWM on any pin with `analogWriteRange`/`analogWriteFreq`/`analogWriteResolution`, `Serial` and TX-only `Serial1`, `Wire.begin(sda, scl)` on any pins, SPI on GPIO 12–15, `ESP.*` (getResetReason, getChipId, getVcc, restart, deepSleep), `EEPROM.begin/commit`, `RANDOM_REG32`.
- `ESP8266WiFi`, `ESP8266WiFiMulti`, event handlers (`onStationModeGotIP`, …) that run between `loop()` calls and only while the sketch keeps the handler, `ESP8266HTTPClient`, `BearSSL::WiFiClientSecure` with `setTrustAnchors`; the networking classes and EEPROM are shared with the ESP32 through `firmware/shim/cores/esp-common`.
- A warning when `loop()` runs 3.2 s without returning, `delay()` or `yield()`: a real ESP8266's software watchdog would reset the board.
- Firmware ABI 2.1: the reset reason and timer deep sleep. ESP32: `esp_reset_reason()`, `esp_sleep_enable_timer_wakeup()` with `esp_deep_sleep_start()`, the wake-up cause, and `IRAM_ATTR`/`RTC_DATA_ATTR` (the latter do not survive a restart).
- `libraries/compat` now works: a file there replaces the vendored source with the same path at build time. First use: U8g2's ESP8266 font read cut 64-bit pointers.
- Examples Esp8266Blink, Esp8266Fade, Esp8266WiFi, Esp8266Eeprom, Esp8266Oled; tests include an HTTP POST from the ESP8266 to a server the test runs, and the ESP8266 core is checked warning-free under `-Werror`.

## M6 — ESP32 DevKit v1 (2026-11-03)

- Firmware ABI 2.0 (ADR-009): numbered I2C and SPI buses on chosen pins, UART pins, `INPUT_PULLDOWN` and `OUTPUT_OPEN_DRAIN`, PWM with a frequency and resolution, the DAC, a key-value store, restart, and the Wi-Fi and network calls. The Nano behaves exactly as before.
- The shim is split by board family: shared code in `firmware/shim/include` and `src`, the AVR core in `cores/avr`, the ESP32 core in `cores/esp32`.
- ESP32 DevKit v1 board: 30-pin map with GPIO numbers, 3.3 V logic, input-only GPIO 34–39, a 12-bit ADC (ADC2 pins read 0 while Wi-Fi is on), DAC on 25/26, the blue LED on GPIO 2, a board drawing with a Wi-Fi indicator. The toolbar's board picker now switches a project's board; wires to pins the other board lacks go, undo brings the old board back.
- ESP32 core (Arduino-ESP32 3.x API): LEDC (`ledcAttach`/`ledcWrite`, and the 2.x channel functions), `analogReadResolution`, `analogReadMilliVolts`, `dacWrite`, `Serial1`/`Serial2` with pins, `Wire.begin(sda, scl)` and `Wire1`, SPI on any pins (VSPI, HSPI), `Serial.printf`, `ESP.*`, `esp_random()` (deterministic), `log_e`. Hardware timers, touch and the temperature sensor fail to compile with a clear message.
- FreeRTOS as cooperative tasks: `xTaskCreate`, delays, queues, semaphores, mutexes, notifications; each task is a thread, only one runs at a time.
- `Preferences` (kept in the project's `preferences.json`), `EEPROM.begin/commit`, `ESP.restart()` (a fresh copy of the firmware runs `setup()` again, `millis()` from 0) and `ESP.deepSleep()`.
- Wi-Fi: a simulated radio (scan, connect with a password check, addresses, events, soft AP) with the networks in the project settings (default: one open network, `ArduSim`); `WiFiClient`, `WiFiClientSecure` (TLS by the computer), `WiFiServer` (127.0.0.1 only), `WiFiUDP` and `HTTPClient` over the computer's real network. While connections are open the run is held at 1×. The headless Runner takes `--no-network`.
- Examples Esp32Blink, Esp32Fade, Esp32Tasks, Esp32WiFi, Esp32Preferences, Esp32Oled; the bundled display libraries run on the ESP32 unchanged.
- Fixed: switching the pacing limit restarted pacing from a stale time, so the run slept for seconds.

## M5 — OLED and polish, MVP (2026-11-02)

- SPI: the board's SCK/MOSI nets form a bus; a byte reaches the device whose CS is LOW, at 8 bit times of the SPI clock (F_CPU/2 to /128). Nobody selected reads 0xFF, two selected is a conflict warning, modes 1 and 2 warn. SPI library in the shim with the AVR constants, `SPISettings` and transactions. Bit-banged SPI works on any pins.
- OLED controllers: SSD1306 with the datasheet command set (page, horizontal and vertical addressing, column and page ranges, start line, remap, scan direction, COM pins, multiplex, offset, contrast, invert, entire-on, charge pump, all scroll commands stepping with the frame clock) and SH1106 (132 columns, page mode, DC-DC and pump commands). Golden tests replay the Adafruit init streams.
- OLED part: SSD1306 128x64, 128x32, 64x48 or 96x16 and SH1106 1.3", over I2C (0x3C/0x3D) or 4-wire SPI with D/C, RES and CS; white, blue or yellow-blue panels; it needs VCC and GND and resets when RES is low. Palette presets for the five usual modules. Pixels are drawn at whole device pixels with a soft glow, sharp at every zoom.
- Bundled libraries Adafruit_GFX 1.13.0, Adafruit_BusIO 1.17.4, Adafruit_SSD1306 2.5.17, Adafruit_SH110X 2.1.15, U8g2 2.37.1 and GyverOLED 1.6.4; examples OledAdafruit (the library's `ssd1306_128x64_i2c` unchanged), OledU8g2, OledGyver and Sh1106. U8g2 and GyverOLED draw pixel-exact on SSD1306 and SH1106 over I2C and SPI in the tests; the Adafruit software-SPI example works too.
- Serial Plotter tab: numbers from Serial lines (space, tab or comma separated, optionally `label:value`), one colored line per value with a legend, auto-scaled axis, pause and clear. New example Potentiometer: the knob in the plotter and an LED dimmed with `analogWrite`.
- Light theme, chosen in project menu → Параметры and remembered; the canvas, code editor, plotter and wires follow it.
- The start view shows a thumbnail of each example's scene.
- Project menu → "О точности симуляции": a side sheet with the fidelity limits; README lists the same.
- The headless Runner prints OLED changes (`oled1: 1024 px on`) and `--dump-display screen.png` saves every display as a PNG.
- Faster dragging in big scenes: wires that did not move are no longer redrawn (60 fps with 50 parts and 200 wires). A performance test checks at least 2 M host calls per second.
- Fixed: the status bar's actual speed lagged after a speed change (it was the average since the start of the run).

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
