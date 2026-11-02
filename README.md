# ArduSim

A Windows desktop simulator for Arduino sketches. It compiles your `.ino` with a bundled portable C++ compiler — no Arduino IDE or arduino-cli — runs it against a virtual board, and shows the result on a schematic with real wires and live displays (HD44780 LCDs, SSD1306/SH1106 OLEDs).

**Status: M0 — scaffold and plans.** Nothing simulates yet; see `docs/PLAN.md`.

## Build and run

Requires Windows 10/11 and the .NET 10 SDK.

```
dotnet build ArduSim.sln
dotnet test
dotnet run --project src/ArduSim.App
```

## How it works

The sketch and our Arduino core shim are compiled into a native `firmware.dll`. A separate Runner process loads it and runs it under a deterministic virtual clock; every Arduino API call (`digitalWrite`, `Wire`, `SPI`, `Serial`, …) goes to the simulator, which models nets, buses and the display controllers at the protocol level. Details: `docs/ARCHITECTURE.md`.

## About simulation accuracy

ArduSim simulates the Arduino API, not the AVR CPU. Expect these differences from real hardware:

- `int` is 32-bit and `double` is 64-bit (on AVR: 16 and 32 bits). `long` stays 32-bit, so `millis()` wraps as on AVR.
- No cycle accuracy: time passes only inside Arduino API calls, using a per-board cost table. Pure computation takes no virtual time.
- Interrupt handlers are not preemptive: they run at the next Arduino API call or at the end of `loop()`.
- No register-level timers: writes to timer, ADC or USART registers log a warning and have no effect.
- A null pointer dereference or integer division by zero stops the run with a crash report (on AVR they silently misbehave).

## Licenses

See `THIRD_PARTY.md`.
