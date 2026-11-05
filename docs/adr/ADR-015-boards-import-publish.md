# ADR-015: Uno, Mega 2560, Pro Mini, PlatformIO import and publishing (M11)

- Status: Accepted.
- Date: 2026-11-05

## Decision

### Boards

All three use the AVR core and Nano timing table v1 (partly estimated, ADR-005).

- **Uno R3** (`arduino-uno-r3`, variant `uno`): the Nano's ATmega328P without A6 and A7, so `analogRead(A6)` fails to compile. Headers as on the board.
- **Mega 2560** (`arduino-mega-2560`, variant `mega`): 54 digital and 16 analog pins, PWM on 2-13 and 44-46, I2C on 20/21, SPI on 50-53, six external interrupts, 4 KB EEPROM, `Serial1`-`Serial3`. `PORTx`/`DDRx`/`PINx` for ports A-L use the ATmega2560's real pin map (`registers.cpp`), so `PORTA = 0xFF` drives D22-D29.
- **Pro Mini** (`arduino-pro-mini-5v`, `arduino-pro-mini-3v3`, variant `nano`, the same pin map): 5 V at 16 MHz, or 3.3 V at 8 MHz with every call in the timing table twice as long and SPI dividers from 8 MHz. Power comes in as `VCC`; `RAW` is not driven. A4-A7 are inner pads, drawn at the right.

`BoardProfile.Uarts` gives each port's pins, for the logic analyzer. Board macros `ARDUINO_AVR_NANO`, `ARDUINO_AVR_UNO`, `ARDUINO_AVR_MEGA2560` and `ARDUINO_AVR_PRO` are defined, as sketches test them to pick pins. `__AVR__` stays undefined (libraries would take register paths).

### PlatformIO import

Opening a folder with `platformio.ini` and no `project.simproj.json` imports it in place: the project file goes next to `platformio.ini`, the sources stay in `src`, and the folder keeps working with PlatformIO. Nothing is copied, and no new UI: «Открыть папку…» does it.

`Import.PlatformIoProject` reads the first of `default_envs`, else the first `[env:...]`, with `[env]`, `extends` and `${section.key}`:

- `board` maps to an ArduSim board by a table; other boards and non-Arduino frameworks are refused with the reason.
- `src_dir`, `include_dir`, `lib_dir`, `lib_extra_dirs`, `monitor_speed`, `-D` and `-I` flags are taken.
- `lib_deps` are not downloaded. Libraries come from `lib`, `.pio/libdeps/<env>` when PlatformIO has run there, and the bundled ones; names found nowhere are listed in a toast. The build then says which include fails.

Two new optional settings carry this in the project file, absent otherwise, so the schema version stays 1: `settings.defines` and `settings.includePaths`.

A sketch folder with no `.ino` is now a plain C++ project: every source under it compiles, nothing is preprocessed, and `main.cpp` includes `Arduino.h` itself. A library with `src/` and no `library.properties` counts as the recursive layout when no header sits in its root (PlatformIO's `lib/<name>/src`).

### Publishing

`tools/publish/Publish.ps1` publishes App and Runner self-contained for win-x64 into one folder (about 220 MB) with the shim, the libraries and the examples. It needs no .NET SDK or runtime to run. The C++ toolchain is not in it: the first sketch build downloads it, as always (ADR-002).

## Consequences

- Uno and Mega `SDA`/`SCL` duplicate headers are not drawn: use A4/A5 and 20/21.
- Hardware timers and other registers stay unsimulated on the Mega too, and its `digitalWrite` costs the same as the Nano's although the real one is slower.
- A PlatformIO project's `lib_deps` need PlatformIO to have run once unless the library is bundled.
- The published folder was smoke-tested on the dev machine with the SDK off the PATH, not on a clean VM.
