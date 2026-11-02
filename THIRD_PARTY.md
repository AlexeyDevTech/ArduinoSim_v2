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

Images, archives and CI files of these repositories are left out; nothing else is changed.

## Icons

The Windows icon font (Segoe Fluent Icons, Segoe MDL2 Assets on Windows 10), part of Windows; nothing is bundled.
