# ArduSim

A Windows desktop simulator for Arduino sketches. It compiles your `.ino` with a bundled portable C++ compiler — no Arduino IDE or arduino-cli — runs it against a virtual board, and shows the result on a schematic with real wires and live displays (HD44780 LCDs, SSD1306/SH1106 OLEDs, seven-segment and TM1637 digits, MAX7219 matrices, WS2812 LEDs).

**Status: M10 — displays** (after the M5 MVP, the ESP32 in M6, the ESP8266 in M7, inputs and sensors in M8, outputs and modules in M9). Every acceptance item of `docs/BRIEF.md` §7 is checked on the development machine; the pass on a clean Windows VM is still open. See `docs/PLAN.md`.

## What it does

- **Boards**: Arduino Nano, ESP32 DevKit v1 (FreeRTOS tasks, LEDC PWM, DAC, Preferences) and the ESP8266 boards NodeMCU v3 and Wemos D1 mini (software PWM, EEPROM in flash); both ESP families have ESP.restart, deep sleep and a simulated Wi-Fi radio with the computer's real network behind it. The board picker in the toolbar switches a project's board.
- **Schematic editor**: LEDs, RGB LED, buttons, slide switch, potentiometer, resistors, buzzer, rotary encoder, 4x4 keypad, joystick, DHT11/DHT22, DS18B20 (1-Wire, several on one bus), HC-SR04, IR receiver and IR remote, SG90 servo, relay module, seven-segment display (1 or 4 digits), TM1637, MAX7219 8x8 matrix (one module or FC-16), WS2812B strip, ring and matrix, DS3231 RTC, microSD module (its files are a folder of the project), LCD 1602/2004 (I2C or parallel; A00 or A02 character ROM), OLED SSD1306 (128x64, 128x32, 64x48, 96x16), SH1106, SSD1309 and grayscale SSD1327 (I2C or SPI), color TFTs ST7735, ST7789 and ILI9341, Nokia 5110 (PCD8544). Pin-to-pin wires with orthogonal routing, undo/redo, copy/paste.
- **Code**: tabs with C++ highlighting, build errors on the line, reload when the file changes in another editor (VS Code and the like).
- **Run**: Build/Run/Pause/Stop, single steps, speed 0.1×–10× or as fast as possible. Buttons, switches, pots, the encoder, keypad, joystick and IR remote stay live during a run; sensor readings change in Properties or by dragging the sensor.
- **Output**: Serial Monitor with input, a Serial Plotter (numbers per line, optionally `label:value`), a build tab and a log of warnings (floating inputs, short circuits, I2C NACKs with the address) that point at the part.
- **Libraries bundled**: LiquidCrystal, LiquidCrystal_I2C, Adafruit_GFX, Adafruit_BusIO, Adafruit_SSD1306, Adafruit_SH110X, U8g2, GyverOLED, Keypad, DHT sensor library (with Adafruit Unified Sensor), OneWire, DallasTemperature, IRremote, Adafruit_NeoPixel, FastLED, SevSeg, TM1637, LedControl, MD_MAX72XX, MD_Parola, RTClib, SdFat, Adafruit ST7735/ST7789, Adafruit ILI9341, Adafruit PCD8544, Adafruit SSD1327, plus Wire, SPI, EEPROM, Servo (ESP32Servo on the ESP32) and SD in the core.
- **Look**: dark theme by default and a light one (project menu → Параметры), Russian UI.

## Build and run

Requires Windows 10/11 and the .NET 10 SDK. The first sketch build downloads the C++ toolchain (llvm-mingw, 182 MiB, checksum-verified) into `%LOCALAPPDATA%\ArduSim\toolchain`.

```
dotnet build ArduSim.sln
dotnet test
dotnet run --project src/ArduSim.App
```

Pick an example on the start screen (the gallery shows each scene) and press Run (F5). `examples/README.md` lists what each one shows: Blink, Button, Potentiometer (Serial Plotter), the LCD examples, and the OLED examples for Adafruit, U8g2, GyverOLED and SH1106.

## Headless runs

The Runner builds and runs a project without the app and prints every pin change, part state, Serial line and warning with its virtual time:

```
src\ArduSim.Runner\bin\Debug\net10.0\ArduSim.Runner.exe --project examples\Blink --run-for 3000
```

```
[       0.004 ms] D13 HIGH
[    1000.008 ms] D13 LOW
[    2000.013 ms] D13 HIGH
stopped (time limit) at 3000.017 ms virtual after 0.02 s wall
```

Options: `--no-network` (ESP boards: Wi-Fi is simulated, real connections fail), `--speed <x|max>` (1 = real time), `--input 500:pot1:value=0.2` (change a part at a virtual time; repeatable), `--dump-serial out.txt`, `--dump-display screen.png` (the displays at the end of the run), `--seed <n>`, `--quiet`, `--build-dir <folder>`.

## How it works

The sketch and our Arduino core shim are compiled into a native `firmware.dll`. A separate Runner process loads it and runs it under a deterministic virtual clock; every Arduino API call (`digitalWrite`, `Wire`, `SPI`, `Serial`, …) goes to the simulator, which models nets, buses and the display controllers at the protocol level. A crashing sketch takes down only the Runner; the app shows the line. Details: `docs/ARCHITECTURE.md`.

## About simulation accuracy

ArduSim simulates the Arduino API, not the AVR CPU. The app lists the same limits under project menu → О точности симуляции. Expect these differences from real hardware:

- `int` is 32-bit and `double` is 64-bit (on AVR: 16 and 32 bits). `long` stays 32-bit, so `millis()` wraps as on AVR. Pointers are 64-bit.
- No cycle accuracy: time passes only inside Arduino API calls, using a per-board cost table (`digitalWrite` ≈ 4 µs, `analogRead` ≈ 112 µs, an I2C byte = 9 bits at the Wire clock, a Serial byte = 10 bits at the baud rate). Pure computation takes no virtual time. Part of the Nano table is estimated and not yet calibrated against a real board.
- Interrupt handlers are not preemptive: they run at the next Arduino API call or at the end of `loop()`, never under `noInterrupts()` or inside another handler. A timer interrupt due inside a long call (`analogRead`, a Serial byte) runs at its time.
- No register-level timers: writes to timer, ADC or USART registers log a warning, reads return 0. Libraries that need a periodic interrupt get one from the simulator instead (IRremote's 50 µs receive tick). `DDRx`/`PORTx`/`PINx` work through the pin functions at 4 µs per bit.
- A null pointer dereference or integer division by zero stops the run (on AVR they silently misbehave); the app shows a crash report with the sketch line.
- RAM (2 KB) and flash (32 KB) limits are not checked. EEPROM (1 KB) is kept per project.
- I2C and SPI are master-only; bit-banged I2C is not simulated (bit-banged SPI is). SPI modes 1 and 2 warn: the device models sample on the rising edge, as in modes 0 and 3.
- The electrical model is static: levels, pull-ups, dividers, shorts and floating inputs; no capacitance, transients or currents. PWM is a duty cycle, not a waveform.
- Sensors answer with the datasheets' typical timings (DHT, HC-SR04, IR frames). A DS18B20 reads a write slot 30 µs after its falling edge and holds a 0 for 45 µs: inside the datasheet windows and loose enough for OneWire, which here goes through `digitalWrite`/`digitalRead` (4 µs each on the Nano). Sending IR is not simulated.
- LCDs use the A00 or A02 character ROM and the 5x8 font (5x10 mode draws as 5x8).
- TFTs: gamma, power and frame-rate commands have no effect; multi-byte status reads are byte-aligned; GRAM starts black. Each SPI byte is a simulator call, so large TFTs on a fast ESP32 bus can draw slower than real time. The Nokia 5110 contrast is an approximation of the glass. Touch controllers are not simulated.
- WS2812 LEDs get each frame whole from Adafruit_NeoPixel or FastLED, timed as 1.25 µs per bit; a sketch that bit-bangs the protocol itself does not reach the strip. SPI LED chipsets (APA102) are not simulated. A servo reads the pulse width (544–2400 µs = 0–180°) and moves at once. Seven-segment brightness is averaged over 20 ms.
- The DS3231 starts from the computer's clock (or a fixed time) at every run; only the 1 Hz square wave is simulated. The SD card is a 64 MB FAT16 card in SPI mode, filled from the project's `sd/` folder at Run and written back at Stop (not after a crash); bit-banged SPI to the card is not simulated.
- `sprintf` formats `%f` (avr-libc prints `?`), `round()` returns a `double`, and `sizeof(int)` is 4, which changes the byte layout `EEPROM.put` writes for `int` fields.
- Sleep modes and the watchdog are not simulated: `sleep_cpu()` returns immediately and `wdt_enable()` never resets the board (each logs a warning).
- ESP8266: the software watchdog (3.2 s without yield) only warns; Serial.swap(), Ticker and RTC user memory are not simulated.
- ESP32: FreeRTOS tasks are cooperative (a task gives up the CPU only in delays and waits; the core number is ignored); the Wi-Fi radio is simulated and connections use the computer's real network, holding the run at 1× while open; free heap and flash sizes are fixed numbers; hardware timers, touch and the temperature sensor fail to compile.

## Licenses

The bundled libraries keep their own licenses (BSD and MIT for the OLED, TFT, Nokia 5110, sensor, FastLED, SevSeg, LedControl, RTClib and SdFat libraries, Apache-2.0 for Adafruit Unified Sensor, LGPL-2.1 for LiquidCrystal, Keypad, MD_MAX72XX and MD_Parola, LGPL-3.0 for Adafruit_NeoPixel and TM1637; Keypad's repository also carries a GPL-3.0 file; LiquidCrystal_I2C ships without one). See `THIRD_PARTY.md`.
