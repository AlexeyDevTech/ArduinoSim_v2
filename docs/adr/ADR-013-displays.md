# ADR-013: More displays (M10)

- Status: Accepted (no expensive fork: no ABI or IPC protocol change).
- Date: 2026-11-03

## Decision

### Color TFTs

`Displays.TftController` models the MIPI DCS controllers ST7735S, ST7789V and ILI9341 on the 4-wire serial interface (D/C separates commands from their parameters): GRAM of 18-bit pixels; sleep in/out, display on/off, normal and partial mode, inversion, idle mode, MADCTL (MY, MX, MV and the RGB/BGR order), COLMOD 12/16/18-bit writes, CASET/RASET windows with RAMWR/RAMWRC, vertical scrolling, the status reads libraries print (RDDPM, RDDMADCTL, RDDCOLMOD, RDDSDR, IDs) and the ILI9341 read index (0xD9). Gamma, power and frame-rate commands are accepted and have no effect; multi-byte reads are byte-aligned (no dummy clock).

`TftPanel` describes each module's glass on the GRAM, taken from the rotation 0 the Adafruit libraries use: the GRAM size the chip is strapped for (ST7735: 128x160, 132x162 or 132x132), the glass origin and directions, the panel's color order and whether it is IPS. An IPS panel is normally black: it shows true colors only with INVON, as on the real module; a TN panel shows white while asleep or off, an IPS panel black. GRAM starts black. Modules: ST7735 1.8" (black tab and green tab), 1.44", 0.96" 80x160; ST7789 1.3" 240x240 without CS (ISpiDevice.HasChipSelect), 2.0" 240x320, 1.14" 135x240; ILI9341 2.8" with SDO for reads. The backlight pin dims the glass by its level or PWM duty; the ST7789 modules pull it up.

### Monochrome and grayscale

- `OledController` gains the SSD1309 (datasheet rev 1.1): 7-byte scroll setups with a column range, the one-column content scroll, the command lock (0xFD), no charge pump (0x8D is reported as unknown and its parameter then reads as a command, as on the chip).
- `Ssd1327Controller` (rev 1.8): 128x128 at 4 bits, column/row windows, horizontal or vertical increment, column and nibble remap, COM remap and split, start line, offset, multiplex, the four display modes, the gray table (B8, B9 linear from reset), lock and horizontal scroll. A pixel's light is its level's pulse width relative to GS15. The module's wiring makes the libraries' setting (0xA0 0x51) upright.
- `Pcd8544Controller`: the Nokia 5110's 84x48 controller with both instruction sets. The glass's contrast follows V<sub>LCD</sub> = 3.06 V + 0.06 V x V<sub>OP</sub>: below about 4.5 V nothing shows, full contrast from about 6.5 V, the background darkens above 7.5 V (an approximation of the glass, not from the datasheet). The LIGHT pin's active level is a property.
- HD44780: the A02 ROM (European, with Cyrillic capitals and Latin-1) as an LCD property; its glyphs use all eight dot rows. The glyph data comes from wokwi-elements (MIT), rasterized from the datasheet.

### Frames, rendering, headless

New frame kinds `GrayOled` (a light byte per pixel), `GraphicLcd` (a darkness byte per pixel, contrast included; Contrast is the backlight) and `ColorTft` (R, G, B per pixel as the glass shows them; Contrast is the backlight). They go over IPC in the existing `DisplayData`. The App draws them with `Views.TftScreen` (TFT and graphic LCD) and `OledScreen` (gray levels). Headless runs print `N px lit` (TFT, non-black pixels), `N px dark` (graphic LCD) and `N px on` (grayscale OLED); `--dump-display` writes color PNGs for TFTs.

### Libraries and the shim

Bundled: Adafruit ST7735 and ST7789 1.12.0, Adafruit ILI9341 1.7.0, Adafruit PCD8544 2.0.3, Adafruit SSD1327 1.0.4; SSD1309 and SSD1327 also through U8g2. The shim gains `wiring_private.h` and, on ESP32, `SPI_MSBFIRST`/`SPI_LSBFIRST`. `libraries/compat/Adafruit_BusIO/Adafruit_SPIDevice.h` turns off BusIO's fast pin I/O under the simulator (on ESP boards it writes GPIO registers through `portOutputRegister`).

### Speed

A hardware SPI byte costs about 0.3 µs of wall time after JIT warm-up (measured), so an ILI9341 full fill takes about 50 ms of wall time against 154 ms of virtual time on a Nano at 8 MHz: no SPI buffer call in the ABI. On an ESP32 at 40 MHz large fills run slower than real time. `SpiBus.Transfer` no longer allocates per byte.

## Consequences

- Touch controllers (XPT2046 and others), TFT_eSPI, parallel 8-bit TFT shields and e-paper are not simulated.
- A sketch can show a picture that is rotated, mirrored, offset or in swapped colors on the canvas exactly when it would on the module (wrong tab, missing INVON, wrong rotation).
