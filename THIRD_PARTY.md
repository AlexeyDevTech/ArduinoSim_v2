# Third-party components

## NuGet packages

| Package | Version | License | Used by |
| --- | --- | --- | --- |
| AvalonEdit | 6.3.1.120 | MIT | App |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | App |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | App |
| Serilog | 4.4.0 | Apache-2.0 | App |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 | App |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | App |
| MessagePack | 3.1.12 | MIT | Ipc |
| xunit.v3 | 4.0.2 | Apache-2.0 | tests |

## Toolchain (downloaded at first run, not redistributed in the repo)

llvm-mingw 20261020 (UCRT, x86_64) by Martin Storsjö — LLVM (Apache-2.0 with LLVM exceptions), mingw-w64 (ZPL/public-domain headers and runtime, see its COPYING), libc++/libunwind (Apache-2.0 with LLVM exceptions).

## Vendored Arduino libraries

Sources only, unmodified, in `libraries/`; compiled by the user's builds.

| Library | Version | Source | License |
| --- | --- | --- | --- |
| LiquidCrystal | 1.0.7 (a89b4d4) | github.com/arduino-libraries/LiquidCrystal | LGPL-2.1-or-later (see its README.adoc) |
| LiquidCrystal_I2C | 1.1.2 (5d01111) | github.com/johnrickman/LiquidCrystal_I2C (Frank de Brabander; Arduino Library Manager entry) | None stated by the author. Bundled as distributed through the Arduino Library Manager, on the owner's decision of 2026-11-02; the prebuilt `LiquidCrystal_I2C.o` of the repository is not included. Settle before any public release. |
| Adafruit_GFX | 1.13.0 (85ce269) | github.com/adafruit/Adafruit-GFX-Library | BSD (license.txt) |
| Adafruit_BusIO | 1.17.4 (3b83642) | github.com/adafruit/Adafruit_BusIO | MIT (LICENSE) |
| Adafruit_SSD1306 | 2.5.17 (d94f699) | github.com/adafruit/Adafruit_SSD1306 | BSD (license.txt) |
| Adafruit_SH110X | 2.1.15 (f29762e) | github.com/adafruit/Adafruit_SH110X | BSD (license.txt) |
| U8g2 | tag 2.37.1 (bb6ebd1; its library.properties says 2.36.19) | github.com/olikraus/U8g2_Arduino | BSD-2-Clause for the code; the fonts carry their own terms, listed in its LICENSE |
| GyverOLED | 1.6.4 (0daa8ab) | github.com/GyverLibs/GyverOLED | MIT (LICENSE) |
| Keypad | 3.1.1 (d04fa7c) | github.com/Chris--A/Keypad (Mark Stanley, Alexander Brevig) | LGPL-2.1 in the header of every source file; the repository's LICENSE file is GPL-3.0. Bundled as distributed through the Arduino Library Manager, on the owner's decision of 2026-11-03. Settle before any public release. |
| DHT_sensor_library | 1.4.7 (f7d6254) | github.com/adafruit/DHT-sensor-library | MIT (license.txt) |
| Adafruit_Unified_Sensor | 1.1.15 (0a9127a) | github.com/adafruit/Adafruit_Sensor | Apache-2.0 (LICENSE.txt) |
| OneWire | 2.3.8 (72249e2) | github.com/PaulStoffregen/OneWire | MIT-style permission notice in OneWire.cpp |
| DallasTemperature | 4.0.6 (3f570bb) | github.com/milesburton/Arduino-Temperature-Control-Library | MIT (LICENSE) |
| IRremote | 4.7.1 (498dc59) | github.com/Arduino-IRremote/Arduino-IRremote | MIT (LICENSE) |
| Adafruit_NeoPixel | 1.15.5 (d514fc3) | github.com/adafruit/Adafruit_NeoPixel | LGPL-3.0 (COPYING) |
| FastLED | 3.9.20 (a7dda76) | github.com/FastLED/FastLED | MIT (LICENSE) |
| SevSeg | 3.7.2 (82f18e3) | github.com/DeanIsMe/SevSeg | MIT (LICENSE.txt) |
| TM1637 | 1.2.0 (82214be) | github.com/avishorp/TM1637 | LGPL-3.0 (LICENSE) |
| LedControl | 1.0.6 (2714e8b) | github.com/wayoda/LedControl | MIT-style permission notice (LICENSE) |
| MD_MAX72XX | 3.5.1 (0409270) | github.com/MajicDesigns/MD_MAX72XX | LGPL-2.1 (LICENSE) |
| MD_Parola | 3.7.7 (0cf3b50) | github.com/MajicDesigns/MD_Parola | LGPL-2.1 (LICENSE) |
| RTClib | 2.1.4 (5298434) | github.com/adafruit/RTClib | MIT (license.txt) |
| SdFat | 2.3.1 (cda0573) | github.com/greiman/SdFat | MIT (LICENSE.md) |
| Adafruit_ST7735_and_ST7789_Library | 1.12.0 (d568339) | github.com/adafruit/Adafruit-ST7735-Library | MIT (notice in the sources and README.txt) |
| Adafruit_ILI9341 | 1.7.0 (1871d95) | github.com/adafruit/Adafruit_ILI9341 | MIT (README.md; the .cpp header says BSD) |
| Adafruit_PCD8544_Nokia_5110_LCD_library | 2.0.3 (87565cd) | github.com/adafruit/Adafruit-PCD8544-Nokia-5110-LCD-library | BSD (license.txt) |
| Adafruit_SSD1327 | 1.0.4 (6404a44) | github.com/adafruit/Adafruit_SSD1327 | BSD (license.txt) |

Images, archives, CI and editor files (`.github`, `.devcontainer`, `.gitignore`, IRremote's `pictures/`), documentation sites, tooling and test folders (FastLED's `ci`, `tests`, `docs` and its `.rgb` data files; SdFat's `doc`) of these repositories are left out; nothing else is changed. Fixes for the simulator live in `libraries/compat/` (OneWire, IRremote, U8g2, Adafruit_NeoPixel, FastLED, SdFat, Adafruit_BusIO), never in the vendored files; a compat file copied from a library and changed keeps that library's license (LGPL-3.0 for `Adafruit_NeoPixel.cpp`, MIT for the FastLED, SdFat and Adafruit_BusIO copies).

The HD44780 A02 character data in `src/ArduSim.Core/Displays/Hd44780Font.A02.cs` comes from wokwi-elements (github.com/wokwi/wokwi-elements, `src/lcd1602-font-a02.ts`, MIT, Copyright (c) 2020 Uri Shaked), rasterized there from the HD44780U datasheet.

The Arduino `SD` and `Servo` libraries are not bundled; the shim has its own implementations of their APIs (`firmware/shim/libraries/SD` on SdFat, `firmware/shim/libraries/Servo`).

## Icons

The Windows icon font (Segoe Fluent Icons, Segoe MDL2 Assets on Windows 10), part of Windows; nothing is bundled.
