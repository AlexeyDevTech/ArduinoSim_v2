OLED scenes for the integration tests: SSD1306 or SH1106, 128x64, over I2C (0x3C) or SPI
(hardware: D0-D13, D1-D11, CS-D10, DC-D9, RES-D8; `ssd1306-swspi` is wired as Adafruit's
`ssd1306_128x64_spi` example expects). The tests copy a scene and put a sketch from
`sketches/` (or a library example) into it. Every pattern sketch draws a filled 16x8 box at
the top-left corner and one pixel at (127, 63), so the test can check orientation, column
offset and size of the picture exactly.