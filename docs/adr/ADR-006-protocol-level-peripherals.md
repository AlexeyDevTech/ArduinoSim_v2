# ADR-006: Protocol-level peripheral simulation

- Status: Accepted (fixed by the brief, §3.5). SSD1306 scrolling scope and the LCD backpack modelling are **proposed** (brief §8).
- Date: 2026-11-02

## Decision

Devices see signals, never library calls: a display works with any library because the controller's command set is emulated.

### Nets

- Every component pin connects through wires to a net. Per net: strong drivers (outputs HIGH/LOW), weak pulls (`INPUT_PULLUP`, resistors to a rail), analog sources (voltage 0–5 V).
- Resolution is incremental: when a driver changes, only that net is re-resolved, and subscribers are notified only if its level changed.
- No driver and no pull → floating: reads come from the seeded PRNG, one warning per pin per run ("D2 is floating") pointing at the component.
- Two strong drivers disagree → one warning per net per run ("short circuit on net …"), and the net reads as unstable.
- `analogRead` quantizes the net voltage against the reference (`DEFAULT` = 5 V rail, `INTERNAL` = 1.1 V, `EXTERNAL` = AREF net) to 10 bits on Nano.
- `5V`, `3V3`, `GND`, `VIN` are named rail nets. A component whose VCC net is not a suitable rail is inert and flagged. No SPICE, no currents.

### Buses

- **I2C**: a bus is a pair (SDA net, SCL net). `i2c_start(addr, rw)` finds the device with that 7-bit address among devices whose SDA/SCL pins sit on those nets. No device → NACK → `Wire.endTransmission()` returns 2. Missing pull-ups → one informational note. Software (bit-banged) I2C is not in the MVP: a device that sees GPIO edges on its SDA/SCL logs a "software I2C is not simulated" warning instead of silently ignoring them.
- **SPI, hardware path**: `spi_transfer` delivers the byte full-duplex to the device on the MOSI/SCK nets whose CS net is LOW; none → 0xFF returns; more than one → conflict warning. No per-bit edges are generated (speed).
- **SPI, bit-level path**: devices subscribe to edges on their SCK net and sample MOSI according to the SPI mode, so `shiftOut` and the software-SPI constructors of the Adafruit libraries work. Both paths feed the same per-byte device handler.
- **HD44780 parallel**: the controller samples D4–D7 (or D0–D7) and RS/RW on the falling edge of E.

### Display controllers

State machines in `ArduSim.Core`, unit-tested with recorded byte streams against golden framebuffers:

- **HD44780**: full instruction set, 80-byte DDRAM with 16x2 (0x00/0x40) and 20x4 (0x00/0x40/0x14/0x54) row mapping, 8 CGRAM glyphs, 4-bit nibble sequencing including the init sequence, busy flag and address counter read, A00 font ROM.
- **PCF8574**: 8-bit expander with a configurable bit map (default P0=RS, P1=RW, P2=E, P3=backlight, P4–P7=D4–D7, address 0x27; 0x3F and the other common mapping selectable), feeding the HD44780 model.
- **SSD1306**: datasheet command set, GDDRAM 128x64, I2C control byte (0x00/0x40, Co bit), 4-wire SPI with D/C, variants 128x64, 128x32, 64x48, 96x16.
- **SH1106**: 132-column RAM with a 2-column offset, page addressing only, lower/higher column commands.

Framebuffers leave Core as immutable snapshots with a version counter; the UI renders only changed ones (WriteableBitmap, nearest-neighbor scaling).

### Proposed: full SSD1306 scrolling

Implement 0x26/0x27 (horizontal), 0x29/0x2A (vertical + horizontal), 0xA3 (vertical scroll area) and 0x2E/0x2F, not only basic horizontal scroll. The acceptance example `ssd1306_128x64_i2c` calls `startscrollright`, `startscrollleft`, `startscrolldiagright` and `startscrolldiagleft`, so the diagonal commands are needed for it to look right. Scroll steps are driven by the display's frame clock in virtual time (about 107 Hz with the reset oscillator and MUX 64, times the frame-interval setting).

### Proposed: one LCD component with an "interface" property

One `LCD (HD44780)` component with `size` (16x2, 20x4) and `interface` (parallel 4-bit, parallel 8-bit, I2C backpack) properties; the palette offers presets such as "LCD 1602 I2C". Its pins change with the interface. Internally the I2C variant composes the PCF8574 and HD44780 models, so a standalone PCF8574 part is cheap to add later.

## Consequences

- Any display library works without per-library code; the cost is careful datasheet-level models with tests.
- Software I2C (U8g2 `SW_I2C` constructors) fails loudly until a bit-level I2C decoder is added.
