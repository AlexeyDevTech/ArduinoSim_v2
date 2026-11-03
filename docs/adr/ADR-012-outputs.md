# ADR-012: Outputs, LED strips and modules (M9)

- Status: Accepted (the owner's answers of 2026-11-03: ABI 2.3 with a frame call for LED strips; the SD card as our own SD library on SdFat with the card's files in a project folder).
- Date: 2026-11-03

## Decision

### Servo

The shim has its own `Servo` library with the Arduino Servo API (attach, write, writeMicroseconds, read, detach; 544–2400 µs; 12 servos) and an `ESP32Servo.h` that maps to it. As the AVR library does with Timer1, one timer interrupt (ABI 2.2) walks the attached servos: a pin goes HIGH, the timer fires after that servo's pulse width and takes it LOW, the frame is padded to 20 ms. The SG90 part measures the HIGH pulses on its signal wire (544 µs = 0°, 2400 µs = 180°, the Arduino default) and turns its horn; a PWM duty on the wire warns, as its pulse width is not modelled.

To keep interrupt latency at zero, `SimulationEngine.Finish` now ends a host call's cost at each scheduled event in it, with a scheduling point there (a timer tick during a 112 µs `analogRead` runs at its time, as a hardware interrupt preempts).

### LED strips (ABI 2.3)

A WS2812 bit lasts 1.25 µs; pin calls cannot make one. ABI 2.3 appends `led_strip_write(pin, data, length, khz)`: Adafruit_NeoPixel's `show()` and FastLED's clockless controller (through `libraries/compat`) hand the whole frame over; the host charges 8 bit times per byte and gives the frame to the strips on the pin's net (`Buses.LedStripBus`). A strip keeps three bytes (G, R, B) per LED and sends the rest on from DOUT; LEDs the frame does not reach keep their color. The part is a strip, a ring or an 8 x 8 matrix (progressive or serpentine). FastLED 3.9.20 runs on its own "stub" platform on every board; the compat files pick it under the simulator and make its clockless controller call the host. SPI LED chipsets (APA102) are not simulated and say so.

### Displays

- Seven-segment digits (one, or four multiplexed, common cathode or anode): each segment's brightness is the share of time it is forward-biased over a 20 ms frame, as the eye averages a multiplexed display; the text reads segments at least half as bright as the brightest.
- TM1637: the two-wire bus (START and STOP as I2C, LSB first, ACK on the ninth clock), data, address and display-control commands, eight brightness levels, the colon as bit 7 of the second digit. Key scanning warns.
- MAX7219 matrices, one module or the FC-16 chain of four: the 16-bit shift register per device (device 0 next to the input, the rightmost module), LOAD latches, registers from the datasheet, shutdown at power-up, and the digit/segment to LED mapping of MD_MAX72XX's FC16_HW or GENERIC_HW. Hardware SPI and bit-banged clocking (LedControl) both work.
- New display frame kinds: `Segments` (brightness per segment, with the digits as text for headless output), `LedMatrix`, `RgbLeds`.

### Relay

A 5 V module with a low- or high-level trigger: COM moves from NC to NO 10 ms after the coil pulls in, back 5 ms after it drops out (datasheet operate and release times).

### DS3231

On I2C 0x68 with the module's pull-ups. The time starts from the computer's clock (or a fixed time, for reproducible runs) and goes on with virtual time; writing the time registers sets it for the rest of the run (it is not kept between runs; OSF is clear, so `lostPower()` is false). BCD time and date with 12/24-hour mode and century, both alarms with their mask bits raising A1F/A2F and pulling INT (SQW, open drain) low when enabled, the 1 Hz square wave, the temperature registers from a live property. Faster square waves and the 32 kHz output are not simulated.

### SD card

The Arduino SD library is GPL-3.0; the shim has its own `SD` library with the same API on the vendored SdFat 2.3.1 (MIT). The card is modelled on SPI: a 64 MB SDSC card (version 2, byte addressing, CSD version 1) answering the commands SdFat sends (CMD0, CMD8, ACMD41, CMD58, CMD9/10, CMD12/13/16, CMD17/18, CMD24/25 with their tokens, erase, ACMD13/23). Its blocks are kept sparse in memory (`Storage.FatImage`): one FAT16 partition with 2 KB clusters, built when the run starts from a folder of the project (`sd/` by default, a property of the part), with VFAT long names; when the run ends normally the file system is read back (FAT16 or FAT32) and the folder updated: changed files written, files the card was loaded with and no longer has deleted. Folders outside the project are refused. A crash of the sketch loses the run's changes. Properties has a button to open the folder. Bit-banged SPI to the card warns.

### Library detection and compat

`libraries/compat` replacements now also count for library detection: the resolver reads a replacement file's `#include` lines instead of the vendored file's (FastLED's unused SD file system pulled SdFat into every FastLED build). SdFat needs two replacements (its own open flags on every board; a 64-bit-safe pointer print), Adafruit_NeoPixel three (the frame call in `show()`, the ESP32 RMT and ESP8266 bit-bang files emptied), FastLED eight. The ESP32 core gains the ESP-IDF headers libraries include (`esp_idf_version.h` at 5.1.4, `esp_log.h`, `esp_system.h`, `esp_heap_caps.h`, `sdkconfig.h` without any target), so ESP-IDF driver code stays out of the build.

## Consequences

- A sketch that bit-bangs WS2812 itself (cycle-counted loops) does not reach the strip; it has to use a library.
- FastLED's first build compiles about 100 sources (cached afterwards).
- The SD card's folder is the card: what a sketch writes appears there after Stop, and a file the user puts there is on the card at the next run.
