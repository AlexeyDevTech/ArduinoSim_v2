# ArduSim

A Windows desktop simulator for Arduino sketches. It compiles your `.ino` with a bundled portable C++ compiler — no Arduino IDE or arduino-cli — runs it against a virtual board, and shows the result on a schematic with real wires and live displays (HD44780 LCDs, SSD1306/SH1106 OLEDs).

**Status: M2 — the app runs sketches on a live scene** (fixed layout; the schematic editor comes in M3). See `docs/PLAN.md`.

## Build and run

Requires Windows 10/11 and the .NET 10 SDK. The first sketch build downloads the C++ toolchain (llvm-mingw, 182 MiB, checksum-verified) into `%LOCALAPPDATA%\ArduSim\toolchain`.

```
dotnet build ArduSim.sln
dotnet test
dotnet run --project src/ArduSim.App
```

Pick an example (Blink, Button, FloatingInput) and press Run (F5). In Button, click the red button or press Space. Without the app:

```
src\ArduSim.Runner\bin\Debug\net10.0\ArduSim.Runner.exe --project examples\Blink --run-for 3000
```

The Runner prints every pin change, Serial line and warning with its virtual time:

```
[       0.004 ms] D13 HIGH
[    1000.008 ms] D13 LOW
[    2000.013 ms] D13 HIGH
stopped (time limit) at 3000.017 ms virtual after 0.02 s wall
```

`--speed 1` paces the run to real time; `--dump-serial out.txt` saves the Serial output.

## How it works

The sketch and our Arduino core shim are compiled into a native `firmware.dll`. A separate Runner process loads it and runs it under a deterministic virtual clock; every Arduino API call (`digitalWrite`, `Wire`, `SPI`, `Serial`, …) goes to the simulator, which models nets, buses and the display controllers at the protocol level. Details: `docs/ARCHITECTURE.md`.

## About simulation accuracy

ArduSim simulates the Arduino API, not the AVR CPU. Expect these differences from real hardware:

- `int` is 32-bit and `double` is 64-bit (on AVR: 16 and 32 bits). `long` stays 32-bit, so `millis()` wraps as on AVR.
- No cycle accuracy: time passes only inside Arduino API calls, using a per-board cost table. Pure computation takes no virtual time.
- Interrupt handlers are not preemptive: they run at the next Arduino API call or at the end of `loop()`.
- No register-level timers: writes to timer, ADC or USART registers log a warning and have no effect.
- A null pointer dereference or integer division by zero stops the run (on AVR they silently misbehave); from M2 the app shows a crash report with the sketch line.
- `sprintf` formats `%f` (avr-libc prints `?`), `round()` returns a `double`, and `sizeof(int)` is 4, which changes the byte layout `EEPROM.put` writes for `int` fields.
- Sleep modes and the watchdog are not simulated: `sleep_cpu()` returns immediately and `wdt_enable()` never resets the board (each logs a warning).

## Licenses

See `THIRD_PARTY.md`.
