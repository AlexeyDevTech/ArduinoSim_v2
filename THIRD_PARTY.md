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

Planned for M5, each keeping its own license (verified when vendored): Adafruit_GFX, Adafruit_BusIO, Adafruit_SSD1306, Adafruit_SH110X, U8g2, GyverOLED.

## Icons

The Windows icon font (Segoe Fluent Icons, Segoe MDL2 Assets on Windows 10), part of Windows; nothing is bundled.
