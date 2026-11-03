# OneWire compat

`util/OneWire_direct_gpio.h` and `util/OneWire_direct_regtype.h` replace the vendored files of
the same paths (OneWire 2.3.8) at build time. The originals reach GPIO registers directly on AVR,
ESP8266 and ESP32; the simulator has none to reach, and the ESP branches name the real chips'
register blocks. The replacements are the vendored file's own fallback branch for every board:
`pinMode`, `digitalWrite` and `digitalRead`, without its "operation is not guaranteed" warning.
Bus timing then follows the board's timing table (a Nano's `digitalWrite` costs 4 µs), which the
simulated DS18B20 accepts (docs/adr/ADR-011-sensors.md). Re-check when OneWire is updated.