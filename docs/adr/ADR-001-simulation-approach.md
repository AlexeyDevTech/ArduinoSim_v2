# ADR-001: API-level simulation with host-compiled firmware

- Status: Accepted (fixed by the brief, §3.1; ABI v1 details confirmed by the owner on 2026-11-02).
- Date: 2026-11-02

## Context

ArduSim must run unmodified Arduino sketches and their libraries, fast enough that display-heavy sketches animate in real time, without Arduino IDE or arduino-cli. CPU emulation of the ATmega328P (simavr, avr8js) would need avr-gcc and give cycle accuracy we do not need; it would also make ESP32/ESP8266 much harder later.

## Decision

The sketch is compiled by a host C++ compiler (ADR-002) together with our Arduino core shim (`firmware/shim`) into a native x64 `firmware.dll`. There is no CPU emulation. The shim implements the Arduino API in C++ and forwards every hardware interaction to the simulator through a versioned C ABI of function pointers (`firmware/shim/include/sim_abi.h`).

### Firmware exports

| Export | Purpose |
| --- | --- |
| `uint32_t sim_fw_abi_version(void)` | ABI version the firmware was built against (`major << 16 \| minor`). |
| `int32_t sim_fw_init(const SimHostApi*)` | Hands over the host table; returns non-zero if the host is incompatible. |
| `void sim_fw_setup(void)` | Calls the sketch's `setup()`. |
| `void sim_fw_loop(void)` | Calls the sketch's `loop()` once. Returning is a scheduling point. |
| `void sim_fw_dispatch_interrupt(int32_t id)` | Runs the ISR attached to external interrupt `id`. |

### ABI v1

`SimHostApi` starts with `struct_size` and `abi_version`; the host refuses firmware with a different major version, and a minor bump only appends functions. All pins use Arduino board numbering (Nano: D0–D13 = 0–13, A0–A7 = 14–21). Strings are UTF-8.

Functions from the brief, unchanged: `pin_mode`, `digital_write`, `digital_read`, `analog_read`, `analog_write`, `tone`, `no_tone`, `now_us`, `delay_us`, `yield`, `serial_write`, `serial_read`, `serial_available`, `i2c_start`, `i2c_write`, `i2c_read`, `i2c_stop`, `spi_begin`, `spi_transfer`, `spi_settings`, `eeprom_read`, `eeprom_write`, `attach_interrupt`, `detach_interrupt`, `log`.

Additions to the brief's list, each needed by a shim feature in the brief:

| Addition | Why |
| --- | --- |
| `analog_reference(mode)` | `analogReference()` changes the ADC scale; only the host knows AREF's net voltage. |
| `serial_begin(port, baud, config)`, `serial_end` | TX drain timing needs the baud rate and frame length (8N1 = 10 bits). |
| `serial_peek`, `serial_available_for_write`, `serial_flush` | `Stream::peek`, `HardwareSerial::availableForWrite`, `flush()` (blocks until TX drained). |
| `i2c_begin`, `i2c_end`, `i2c_set_clock(hz)` | `Wire.setClock` must change the per-byte cost. |
| `spi_end` | `SPI.end()`. |
| `set_interrupts_enabled(enabled)` | `noInterrupts()`/`interrupts()`: the host must not dispatch ISRs while masked and dispatches pending ones on re-enable. |
| `fatal(message)` | Shim-detected unrecoverable errors (pure virtual call, `new` failure) end the run with a clear report. |
| `crash_info` (pointer) | Memory shared with the App where the firmware's vectored exception handler records the exception status, address, faulting data address and a return address into the sketch before the process dies (ADR-003). Fields are only appended; the host provides at least 64 bytes. |

`serial_write` takes a buffer (`port, data, length`) rather than one byte so `Print::write(buf, n)` costs one transition.

### Static constructors

Global C++ objects in the sketch are constructed inside `LoadLibrary`, before `sim_fw_init` runs, and real libraries talk to hardware there (the parallel `LiquidCrystal` constructor calls `pinMode` and `begin()`). The Runner therefore publishes the address of its `SimHostApi` in the process environment variable `ARDUSIM_HOST_API` (hex) before loading the DLL; the shim resolves it lazily on the first host call via `GetEnvironmentVariableA` and validates the struct header. `sim_fw_init` still performs the formal handshake. The M0 smoke test verified this path with both toolchains.

### Compile-time environment

Predefined macros exactly as the brief lists (`ARDUINO=10819`, `ARDUINO_ARCH_SIM`, `SIM_BOARD_NANO`, `F_CPU=16000000L`, …); `__AVR__` and `ARDUINO_ARCH_AVR` are not defined. Libraries without a portable path fail to compile and get a shim in `libraries/compat/`. The shim never exposes `<windows.h>` to sketch translation units (its `min`/`max` macros clash with Arduino's).

Direct port access (`DDRx`/`PORTx`/`PINx`) uses proxy objects routed to the pin API; timer, ADC and USART registers are proxies that log "register X is not simulated" once and read 0.

## Consequences

- Any library that compiles for a generic Arduino target runs at native speed. The smoke test measured ~5 ns per host call (~190 M calls/s) against a target of 2 M/s, so the budget goes to the simulation, not to the transition.
- Known fidelity limits, shown in README and in the app's "About simulation accuracy" panel: `int` is 32-bit and `double` 64-bit (AVR: 16/32); `long` stays 32-bit (Windows LLP64), so `millis()` wraps as on AVR; no cycle accuracy; ISRs are not preemptive; no register-level timers. Integer division by zero and null dereferences crash the run (on AVR they silently produce garbage); the crash report names the line.
- The ABI is the most expensive interface to change: it is versioned and covered by a shim test harness from M1.
