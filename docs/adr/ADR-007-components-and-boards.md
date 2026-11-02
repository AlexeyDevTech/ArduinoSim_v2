# ADR-007: Components and boards

- Status: Accepted (fixed by the brief, §3.6).
- Date: 2026-11-02

## Decision

A component has three parts:

1. **Definition** (data, in `ArduSim.Core`): id, display name key, category, pins (name, kind: digital, analog, power, ground, bus role), properties with editor metadata (type, range, enum values, default), vector art as SVG-subset path data. Data only, so Core stays UI-agnostic.
2. **Behavior** (`ISimComponent`, in `ArduSim.Core`): binds to nets, reacts to net changes, schedules events on the virtual clock, exposes live state for the view.
3. **View** (in `ArduSim.App`): renders the art plus live state (LED brightness from PWM duty, pressed button, pot knob angle, display content).

Components register through an attribute (`[SimComponent("led")]`) discovered at startup. Trivial parts (resistor, LED colors) are data-driven definitions bound to a shared behavior.

### Arduino Nano v3 (MVP board)

D0–D13, A0–A7 (A6/A7 analog-only), 5V, 3V3, VIN, GND ×2, RST, AREF. PWM on 3, 5, 6, 9, 10, 11; I2C on A4 (SDA) / A5 (SCL); SPI on 11 (MOSI), 12 (MISO), 13 (SCK), SS 10; UART on 0/1; external interrupts 0/1 on pins 2/3; built-in LED on D13; 10-bit ADC; 5 V logic; 1024-byte EEPROM.

Board profiles live in `ArduSim.Boards`: pin map and capabilities, ADC resolution and reference voltages, logic level, interrupt pin map, bus pin assignments, EEPROM size, timing table (ADR-005) and the predefined macros for the compiler. Later profiles (Uno, ESP32 DevKit, ESP8266 NodeMCU / Wemos D1 mini) add their own pin names, 3.3 V logic, 12-bit ADC, more UARTs and `ledc` PWM.

## Consequences

- Adding a part means a definition, a behavior (or reuse of a shared one) and, if needed, a view template; no change to the canvas or IPC.
- Live component state travels in snapshot deltas keyed by component id (ADR-003).
