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
| Potentiometer | analogRead in the Serial Plotter (Плоттер tab), analogWrite dimming an LED |
| OledAdafruit | Adafruit's own `ssd1306_128x64_i2c` example, unchanged: drawing, text, bitmaps, scrolling (SSD1306 128x64 at 0x3D) |
| OledU8g2 | U8g2 in full-buffer mode on an SSD1306 128x64 over I2C: text, a frame, a bouncing ball and a seconds counter |
| OledGyver | GyverOLED (buffered) on an SSD1306 128x64 over I2C: big text, a frame and a counter |
| Sh1106 | Adafruit_SH110X on a 1.3" SH1106 over hardware SPI (D13 SCK, D11 MOSI, D10 CS, D9 DC, D8 RES) |
