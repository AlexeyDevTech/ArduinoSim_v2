# ADR-011: Inputs, sensors and timer interrupts (M8)

- Status: Accepted (the owner's answers of 2026-11-03: ABI 2.2 with timers; sensor readings editable both in Properties and by a gesture on the part; a separate IR remote part; Keypad bundled as distributed).
- Date: 2026-11-03

## Decision

### Parts drive their nets in virtual time

Sensors with a timed one-wire protocol are parts that watch their data net and schedule their own edges on the engine's clock (`SimulationClock.Schedule`). The firmware sees them through ordinary `digitalRead`/`pulseIn` calls, which advance virtual time by the board's timing table, so the libraries' own bit-banging code runs unchanged:

- **DHT11 / DHT22** (`dht`, model property): the board holds DATA low (DHT22: at least 0.8 ms, DHT11: 18 ms) and releases it; 30 µs later the sensor sends 80 µs low, 80 µs high and 40 bits of 50 µs low plus 26 µs (0) or 70 µs (1) high, as the datasheets give. A start that is too short for the model is answered by nothing and one warning that names the model to put in the sketch. DHT11 readings are clamped to its 0–50 °C, 20–90 % range, with a warning.
- **HC-SR04**: a TRIG pulse of at least 10 µs; 460 µs after it ends ECHO is HIGH for 58.3 µs per cm (343 m/s, there and back); beyond 400 cm no echo, ECHO HIGH for 38 ms.
- **IR receiver** (VS1838B/TSOP style): OUT pulled up internally (30 kΩ), driven LOW during each mark of a demodulated frame.

### 1-Wire bus model

`Buses/OneWireSlave` is the slave side of 1-Wire, timed from the DQ net's edges: a low of 480 µs or more is a reset, answered 30 µs after the rise by a 120 µs presence pulse; for 300 µs after a reset every edge belongs to presence pulses (a second device's presence is not a write slot). Any other falling edge starts a time slot. A listening device takes the bit as the line's state 30 µs after the edge; a talking device sends a 0 by holding the line low for 45 µs. Several devices on a net share it by wired-AND through their own drivers, which is what search ROM needs. The ROM layer (read, match, skip, search, alarm search) is in the base class; function commands are the device's.

The DS18B20 has the scratchpad, the EEPROM copy (in memory for the run), 9–12 bit resolution with the datasheet conversion times (93.75 ms × 2^(bits−9)), the +85 °C power-on value, alarm flags (factory TH 75 °C, TL 70 °C) and parasite power (VDD on GND; the board must hold DQ high during a conversion). Its 48-bit serial number is a hash of the part id, stable across runs. DQ without a pull-up floats and the part says so.

Timing tolerance is a modelling choice: the hold time and the 30 µs sample point sit inside the datasheet windows (15–60 µs) and leave room for the Nano's 4 µs `digitalWrite`/`digitalRead` (the OneWire compat shim uses the Arduino calls; real AVR register access takes a fraction of a microsecond).

### Inputs

Rotary encoder (KY-040: contacts to GND with 10 kΩ pull-ups on CLK and DT; one quadrature cycle per detent, a transition every 2 ms, CLK leading clockwise; queued detents), 4x4 membrane keypad (a key is a switch between its row and column pin), analog joystick (two 10 kΩ potentiometers and a button), and the IR remote: the 21-key remote of the common kits, sending NEC frames (address 0x00) with repeat frames every 108 ms while a key is held. The remote has no pins: `ComponentContext.Infrared` (`InfraredLink`) carries its frames to every powered IR receiver in the scene.

### Live sensor readings (UI)

A property marked `Live` (temperature, humidity, distance) stays editable while the simulation runs: in Properties as a text field and a slider, and on the part by a vertical drag (Shift: the second reading, DHT humidity) or the wheel. While running, the change goes to the part as an input of the same name; it is not saved and not undoable, and after Stop the part shows the saved value again (as the potentiometer's knob). Everything else stays locked while running (docs/UI.md). Encoder: wheel or drag around the knob turns it, a click without moving presses the shaft (holding still for 250 ms holds it). Joystick: drag the cap, it springs back on release; a click without moving presses it. Keypad and remote: press and hold keys with the mouse.

### Timer interrupts (ABI 2.2)

IRremote samples its receiver pin in a 50 µs timer interrupt. ABI 2.2 appends `timer_start(timer, period_ns)` and `timer_stop(timer)` to `SimHostApi` (four timers, `SIM_TIMER_COUNT`); the host raises `SIM_TIMER_INTERRUPT_BASE + timer` through the existing `sim_fw_dispatch_interrupt`, at scheduling points, after external interrupts, never while interrupts are off or an ISR runs. A tick that finds the previous one still pending is lost, as a hardware flag loses it. Periods below 5 µs are raised to 5 µs with a warning. `delay()` now ends a slice at the next scheduled event, so the interrupts events raise run on time inside it. The shim wraps the calls as `sim/timers.h` (`sim_timer_claim`, `sim_timer_start`, `sim_timer_stop`); later work (ESP32 `timerBegin`, ESP8266 `timer1`/Ticker, the Servo shim in M9) builds on them.

### Libraries and compat

Vendored unmodified: Keypad 3.1.1, DHT sensor library 1.4.7 with Adafruit Unified Sensor 1.1.15, OneWire 2.3.8, DallasTemperature 4.0.6, IRremote 4.7.1. `libraries/compat` now replaces headers too: the builder writes a clang VFS overlay (`compat-overlay.json` in the build folder) that maps each vendored path to its replacement, so the library's own `#include "…"` lines find it. OneWire gets its fallback GPIO branch (Arduino calls) on every board; IRremote gets an `IRTimer.hpp` that takes the receive tick from a simulated timer. Sending IR by timer PWM is not simulated (no IR LED part): the send pin stays low, with a warning.

## Consequences

- A sketch's own bit-banged protocols for these sensors work too; protocol timing outside the datasheet windows fails the way it would on hardware (no answer, a timeout, a wrong bit).
- Timer interrupts every 50 µs cost host calls; at full speed an IRremote sketch still runs many times faster than real time on the dev machine.
- The Keypad library's files say LGPL-2.1 while its repository's LICENSE file is GPL-3.0; it is bundled as distributed (THIRD_PARTY.md), to be settled before a public release like LiquidCrystal_I2C.
