# Per-library compatibility shims

One folder per library that has no portable (non-AVR) code path. A file at `compat/<library folder>/<path>` replaces the vendored file with the same relative path, header or source, when the firmware is built. The vendored file stays untouched: clang reads the replacement through a VFS overlay (`compat-overlay.json` in the build folder), so the library's own `#include "..."` lines find it too. Library detection reads a replacement's `#include` lines in place of the vendored file's, so a replacement can also cut a dependency. Each library folder has a README saying what is replaced and why.

- `U8g2`: `src/clib/u8x8_8x8.c`, a 64-bit-safe ESP8266 font read.
- `OneWire`: `util/OneWire_direct_gpio.h` and `util/OneWire_direct_regtype.h`, pin access through the Arduino calls on every board.
- `IRremote`: `src/private/IRTimer.hpp`, the 50 µs receive tick from a simulated timer interrupt.
- `Adafruit_NeoPixel`: `Adafruit_NeoPixel.cpp` (`show()` hands the frame to the simulator, ABI 2.3), `esp.c` and `esp8266.c` emptied.
- `FastLED`: eight files that put every board on FastLED's own stub platform, whose clockless controller hands the frame to the simulator; the ESP32 drivers off; an unused SD file system emptied.
- `SdFat`: `src/SdFatConfig.h` (SdFat's own open flags on every board) and `src/iostream/ostream.h` (a 64-bit-safe pointer print).
