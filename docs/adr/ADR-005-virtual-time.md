# ADR-005: Cooperative, deterministic virtual time

- Status: Accepted (fixed by the brief, §3.3; timing table v1 confirmed by the owner on 2026-11-02, calibration still open).
- Date: 2026-11-02

## Decision

The firmware runs on the Runner's single simulation thread, and virtual time advances only inside host API calls.

- **Clock**: `ulong` nanoseconds internally (an I2C bit at 400 kHz is 2.5 µs; µs resolution would accumulate rounding). `now_us` returns `ns / 1000`.
- **Cost accounting**: every `SimHostApi` call charges a cost from the board's timing table before it returns.
- **delay(n)** advances in 100 µs slices and runs due scheduled events after each slice. Returning from `loop()` and `yield()` are scheduling points, as is every host call.
- **Scheduler**: a priority queue keyed on `(virtual time, sequence number)`, so events due at the same instant run in insertion order and runs are reproducible. Entries: peripheral timers, serial TX drain, tone end, display frame clocks (SSD1306 scroll).
- **PWM and tone** are state (duty, frequency), not edges.
- **Interrupts**: external-interrupt ISRs are dispatched on the firmware thread at the next scheduling point, never while `noInterrupts()` is active or while an ISR is running. Not preemptive; documented in the accuracy panel.
- **Pacing**: at 1× the Runner compares virtual and wall time at scheduling points (at most every ~1 ms of virtual time) and sleeps when ahead. 0.1×, 0.5×, 2×, 10× scale the target; "max" never sleeps. Pause and step by one `loop()` or by 1/10/100 ms.
- **Determinism**: the simulation never reads wall-clock time or OS randomness. Floating inputs read from a PRNG seeded per run (fixed default seed). UI input events are applied at the next scheduling point and stamped with that virtual time; the same timed input sequence reproduces the same run.
- **Runaway**: no host call for 2 s of wall time while running → `FirmwareBusy` (ADR-003).

## Nano timing table v1

| Host call | Cost | Source / reasoning |
| --- | --- | --- |
| `digital_write`, `digital_read` | 4 µs | Brief; published measurements of the AVR core at 16 MHz fall in 3.4–5 µs (PWM pins are slower because of `turnOffPWM`). |
| `pin_mode` | 4 µs | Estimate: same pin-table lookups as `digitalWrite`. |
| `analog_read` | 112 µs | Brief; 13 ADC clocks at 125 kHz = 104 µs plus setup. |
| `analog_write` | 5 µs | Estimate. |
| `now_us` (`millis`, `micros`) | 1 µs | Brief. |
| `tone`, `no_tone`, `attach_interrupt`, `eeprom_read` | 1 µs | Estimate (short register sequences). |
| `eeprom_write` | 1 µs, then the next EEPROM access waits until 3.3 ms after the write started | ATmega328P datasheet EEPROM programming time. |
| I2C | 9 bit times per byte at the Wire clock (100 kHz default, `Wire.setClock` respected); START and STOP 1 bit time each | Brief. A full SSD1306 frame at 400 kHz ≈ 23–25 ms. |
| SPI | 8 bit times per byte at the SPI clock | Brief. |
| Serial | frame bits per byte at the baud rate (8N1 = 10), via the 64-byte TX ring drained by scheduled events; writes block while the ring is full | Brief. |
| `yield`, `delay_us` overhead | 0 | Scheduling points only. |

Calibration (open): run `tools/nano-timing-bench/nano-timing-bench.ino` on a real Nano and replace the estimates with measured numbers, or cross-check by running the AVR core under avr8js (MIT) with an avr-gcc build, used only as a one-off reference.

## Consequences

- Display refresh rates, Serial throughput and `millis()`-based animations behave realistically without cycle emulation.
- Pure computation inside the sketch costs zero virtual time; a sketch that busy-waits on a computed loop counter (rather than `millis()`/`micros()`) runs faster than on hardware. Listed as a fidelity limit.
