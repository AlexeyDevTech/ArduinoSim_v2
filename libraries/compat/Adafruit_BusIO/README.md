# Adafruit_BusIO compat

One file replaces the vendored file of the same path (Adafruit_BusIO 1.17.4) at build time:

- `Adafruit_SPIDevice.h`: a copy with an `ARDUINO_ARCH_SIM` branch first in the fast pin I/O
  selection, so software SPI goes through `digitalWrite`/`digitalRead` on every simulated board.
  On ESP32 and ESP8266 the original writes the GPIO output registers through
  `portOutputRegister()`, which the simulator does not have (the Nano build already took the
  portable path, since `__AVR__` is not defined there).

The copy keeps the library's MIT license. When the library is updated, take its new
`Adafruit_SPIDevice.h` and put the marked branch back.