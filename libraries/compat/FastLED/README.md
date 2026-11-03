# FastLED compat

FastLED 3.9.20 already has a "stub" platform for host builds (it is what it picks on x86-64). Eight
files replace the vendored files of the same paths at build time so that every simulated board
uses it and it reaches the simulator:

- `src/platforms.h`, `src/led_sysdefs.h`: copies with an `ARDUINO_ARCH_SIM` branch first, so the
  ESP32 and ESP8266 boards take the stub platform instead of their RMT/I2S/bit-bang drivers.
- `src/platforms/stub/led_sysdefs_stub_generic.h`: the stub's system definitions without their own
  declarations of `millis`, `delay` and `pinMode`, whose types clash with the simulator's Arduino.h.
- `src/platforms/stub/led_sysdefs_stub.cpp`: empty; the original compiles host implementations of
  those functions, which the simulator's core already provides.
- `src/platforms/stub/clockless_stub_generic.h`: the clockless controller (WS2812 and the like)
  hands each frame, after brightness, correction and dithering, to `sim_led_strip_write` (ABI 2.3).
- `src/platforms/stub/fastspi_stub_generic.h`: the stub's SPI output for APA102-style chipsets still
  drops the data, but says so once.
- `src/third_party/espressif/led_strip/src/enabled.h`: the ESP32 driver switches, all off under the
  simulator (no RMT, no clockless SPI), so the ESP32 driver sources compile to nothing.
- `src/platforms/fs_sdcard_arduino.hpp`: empty. FastLED never includes it, but its `#include <SD.h>`
  made the library detection (which reads `#include` lines, and reads compat files in place of the
  ones they replace) pull SdFat into every FastLED build.

The copies keep FastLED's MIT license. When FastLED is updated, check that the stub platform still
exists, take the new versions of the copied files and put the `ARDUINO_ARCH_SIM` changes back.
