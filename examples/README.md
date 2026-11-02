# Examples

One folder per example project (ADR-008 layout). Every example is built and run headlessly by `tests/ArduSim.Integration.Tests`.

| Example | Shows |
| --- | --- |
| Blink | D13 and an external LED toggling every second |
| Button | INPUT_PULLUP, debouncing, a toggled LED, Serial |
| FloatingInput | the floating-input warning |
| LcdHello | LiquidCrystal on a 20x4 LCD in 4-bit mode: hello, world!, a seconds counter, a createChar glyph |
| LcdI2c | LiquidCrystal_I2C on a 16x2 LCD with a PCF8574 backpack at 0x27 |
| LcdCustomChars | createChar glyphs and scrollDisplayLeft over I2C |

Planned for M5: OLED Adafruit, OLED U8g2, OLED GyverOLED, SH1106.
