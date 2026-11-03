# Adafruit_NeoPixel compat

Three files replace the vendored files of the same paths (Adafruit_NeoPixel 1.15.5) at build time:

- `Adafruit_NeoPixel.cpp`: a copy of the vendored file with `ARDUINO_ARCH_SIM` branches. `show()`
  hands the whole frame to the simulator (`sim_led_strip_write`, ABI 2.3) instead of the
  per-architecture bit-banging, which needs cycle-exact timing the simulator has no notion of;
  the ESP32 RMT setup in the constructor and destructor is left out. Everything else is unchanged.
- `esp.c` and `esp8266.c`: empty. They drive the ESP32 RMT peripheral and the ESP8266 cycle counter.

The copy keeps the library's LGPL-3.0 license. When the library is updated, take its new
`Adafruit_NeoPixel.cpp` and put the four marked changes back (diff the two copies).