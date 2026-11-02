# ArduSim

A Windows desktop simulator for Arduino sketches. It compiles your `.ino` with a bundled portable C++ compiler — no Arduino IDE or arduino-cli — runs it against a virtual board, and shows the result on a schematic with real wires and live displays (HD44780 LCDs, SSD1306/SH1106 OLEDs).

**Status: M5 — MVP.** Every acceptance item of `docs/BRIEF.md` §7 is implemented and checked on the development machine; the pass on a clean Windows VM is still open. See `docs/PLAN.md`.

## What it does

- **Schematic editor**: Arduino Nano, LEDs, RGB LED, buttons, slide switch, potentiometer, resistors, buzzer, LCD 1602/2004 (I2C or parallel), OLED SSD1306 (128x64, 128x32, 64x48, 96x16) and SH1106 (I2C or SPI). Pin-to-pin wires with orthogonal routing, undo/redo, copy/paste.
- **Code**: tabs with C++ highlighting, build errors on the line, reload when the file changes in another editor (VS Code and the like).
- **Run**: Build/Run/Pause/Stop, single steps, speed 0.1×–10× or as fast as possible. Buttons, switches and pots stay live during a run.
- **Output**: Serial Monitor with input, a Serial Plotter (numbers per line, optionally `label:value`), a build tab and a log of warnings (floating inputs, short circuits, I2C NACKs with the address) that point at the part.
- **Libraries bundled**: LiquidCrystal, LiquidCrystal_I2C, Adafruit_GFX, Adafruit_BusIO, Adafruit_SSD1306, Adafruit_SH110X, U8g2, GyverOLED, plus Wire, SPI and EEPROM in the core.
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

Options: `--speed <x|max>` (1 = real time), `--input 500:pot1:value=0.2` (change a part at a virtual time; repeatable), `--dump-serial out.txt`, `--dump-display screen.png` (the displays at the end of the run), `--seed <n>`, `--quiet`, `--build-dir <folder>`.

## How it works

The sketch and our Arduino core shim are compiled into a native `firmware.dll`. A separate Runner process loads it and runs it under a deterministic virtual clock; every Arduino API call (`digitalWrite`, `Wire`, `SPI`, `Serial`, …) goes to the simulator, which models nets, buses and the display controllers at the protocol level. A crashing sketch takes down only the Runner; the app shows the line. Details: `docs/ARCHITECTURE.md`.

## About simulation accuracy

ArduSim simulates the Arduino API, not the AVR CPU. The app lists the same limits under project menu → О точности симуляции. Expect these differences from real hardware:

- `int` is 32-bit and `double` is 64-bit (on AVR: 16 and 32 bits). `long` stays 32-bit, so `millis()` wraps as on AVR. Pointers are 64-bit.
- No cycle accuracy: time passes only inside Arduino API calls, using a per-board cost table (`digitalWrite` ≈ 4 µs, `analogRead` ≈ 112 µs, an I2C byte = 9 bits at the Wire clock, a Serial byte = 10 bits at the baud rate). Pure computation takes no virtual time. Part of the Nano table is estimated and not yet calibrated against a real board.
- Interrupt handlers are not preemptive: they run at the next Arduino API call or at the end of `loop()`, never under `noInterrupts()` or inside another handler.
- No register-level timers: writes to timer, ADC or USART registers log a warning, reads return 0. `DDRx`/`PORTx`/`PINx` work through the pin functions at 4 µs per bit.
- A null pointer dereference or integer division by zero stops the run (on AVR they silently misbehave); the app shows a crash report with the sketch line.
- RAM (2 KB) and flash (32 KB) limits are not checked. EEPROM (1 KB) is kept per project.
- I2C and SPI are master-only; bit-banged I2C is not simulated (bit-banged SPI is). SPI modes 1 and 2 warn: the device models sample on the rising edge, as in modes 0 and 3.
- The electrical model is static: levels, pull-ups, dividers, shorts and floating inputs; no capacitance, transients or currents. PWM is a duty cycle, not a waveform.
- LCDs use the A00 character ROM and the 5x8 font (5x10 mode draws as 5x8).
- `sprintf` formats `%f` (avr-libc prints `?`), `round()` returns a `double`, and `sizeof(int)` is 4, which changes the byte layout `EEPROM.put` writes for `int` fields.
- Sleep modes and the watchdog are not simulated: `sleep_cpu()` returns immediately and `wdt_enable()` never resets the board (each logs a warning).

## Licenses

The bundled libraries keep their own licenses (BSD and MIT for the OLED libraries, LGPL-2.1 for LiquidCrystal; LiquidCrystal_I2C ships without one). See `THIRD_PARTY.md`.
