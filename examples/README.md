# Examples

One folder per example project (ADR-008 layout). Every example is built and run headlessly by `tests/ArduSim.Integration.Tests`.

| Example | Shows |
| --- | --- |
| Blink | D13 and an external LED toggling every second |
| Button | INPUT_PULLUP, debouncing, a toggled LED, Serial |
| FloatingInput | the floating-input warning |
| ScreensMenu | screens and transitions from the Экраны tab: a menu on a rotary encoder, values from `ui_hooks.cpp`, home from any screen (20x4 LCD on I2C) |
| ScreensKitchenTimer | a whole device from the Экраны tab with no code of its own: clock in big digits, a countdown set on the keypad, a stopwatch, settings in EEPROM, Russian text, a notification with a beep over any screen |
| LcdHello | LiquidCrystal on a 20x4 LCD in 4-bit mode: hello, world!, a seconds counter, a createChar glyph |
| LcdI2c | LiquidCrystal_I2C on a 16x2 LCD with a PCF8574 backpack at 0x27 |
| LcdCustomChars | createChar glyphs and scrollDisplayLeft over I2C |
| Potentiometer | analogRead in the Serial Plotter (Плоттер tab), analogWrite dimming an LED |
| OledAdafruit | Adafruit's own `ssd1306_128x64_i2c` example, unchanged: drawing, text, bitmaps, scrolling (SSD1306 128x64 at 0x3D) |
| OledU8g2 | U8g2 in full-buffer mode on an SSD1306 128x64 over I2C: text, a frame, a bouncing ball and a seconds counter |
| OledGyver | GyverOLED (buffered) on an SSD1306 128x64 over I2C: big text, a frame and a counter |
| Sh1106 | Adafruit_SH110X on a 1.3" SH1106 over hardware SPI (D13 SCK, D11 MOSI, D10 CS, D9 DC, D8 RES) |
| Esp32Blink | ESP32 DevKit v1: the on-board LED (GPIO 2) and an LED on GPIO 4 |
| Esp32Fade | LEDC PWM at 5 kHz, 10 bits: a breathing LED on GPIO 4 |
| Esp32Tasks | two FreeRTOS tasks and a queue beside loop() |
| Esp32WiFi | Wi-Fi scan and connect (simulated radio), an HTTP GET over the real network |
| Esp32Preferences | a boot counter in Preferences across ESP.restart() and across runs |
| Esp32Oled | U8g2 on an SSD1306 at the ESP32's default I2C pins (21/22) |
| Esp8266Blink | NodeMCU v3: the module's LED (GPIO 2, lit while LOW) and an LED on D6 |
| Esp8266Fade | Wemos D1 mini: software PWM on D2 with analogWriteRange(1023) |
| Esp8266WiFi | ESP8266WiFi scan, connect with an event handler, ESP8266HTTPClient GET |
| Esp8266Eeprom | EEPROM.begin/commit boot counter across ESP.restart(), with the reset reason |
| Esp8266Oled | U8g2 on an SSD1306 at D2/D1 of a Wemos D1 mini |
| RotaryEncoder | a KY-040 encoder read with digitalRead: position up and down, the shaft button resets it |
| Keypad4x4 | the Keypad library on a 4x4 keypad (rows D9–D6, columns D5–D2): keys build a code, # enters it |
| Joystick | an analog stick on A0/A1 and its button on D2, as x/y in the Serial Plotter |
| Dht22 | the DHT sensor library: humidity, temperature and heat index every two seconds |
| Ds18b20 | OneWire and DallasTemperature: two DS18B20 on one bus with a 4.7 kΩ pull-up, their addresses and readings |
| Ultrasonic | an HC-SR04 with pulseIn: the distance, "Out of range" beyond 4 m, the LED on D13 under 20 cm |
| IrRemote | IRremote decoding the kit remote's NEC keys and repeats; the play key toggles the LED on D13 |
| ServoKnob | the Servo library: an SG90 on D9 follows the potentiometer on A0 |
| Relay | a 5 V relay module on D7 (low-level trigger) switches an LED once a second |
| SevenSegment | SevSeg multiplexes a 4-digit common-cathode display and counts tenths of a second |
| Tm1637 | TM1637Display: a minutes:seconds timer with a blinking colon |
| MatrixParola | MD_Parola scrolls text on an FC-16 board (four MAX7219 matrices) over hardware SPI |
| MatrixLedControl | LedControl draws a beating heart on one MAX7219 matrix, bit-banged on D12/D11/D10 |
| NeoPixelRing | Adafruit_NeoPixel: a rainbow around a 16-LED WS2812 ring on D6 |
| FastLedMatrix | FastLED: a moving rainbow on an 8x8 serpentine WS2812 panel on D5 |
| Ds3231Clock | RTClib: date, time and temperature from a DS3231 every second; its alarm on SQW interrupts D2 |
| SdCardLog | the SD library: reads config.txt from the card, logs A0 to log.csv; the card is the project's sd folder |
| TftIli9341 | Adafruit_ILI9341 on a 2.8" TFT: the controller's status reads, a test card, a seconds counter |
| TftSt7735 | Adafruit_ST7735 on a 1.8" 128x160 TFT: the potentiometer on A0 as a number and a bar |
| TftSt7789Esp32 | Adafruit_ST7789 on a 1.3" 240x240 IPS without CS on the ESP32: rings and a moving dot |
| Nokia5110 | Adafruit_PCD8544: text, a sine wave and a counter on the Nokia 5110, backlight by PWM |
| OledSsd1309 | U8g2 on a 2.42" SSD1309 over SPI: a frame, a title and a moving bar |
| OledSsd1327Esp32 | Adafruit_SSD1327 on a 1.5" 128x128 grayscale OLED (ESP32, I2C): sixteen gray levels |
| LcdA02 | a 1602 LCD with the A02 ROM: "ПРИВЕТ, МИР!" and Latin-1 letters |
| UnoBlink | an Uno R3: its LED and one on D7 blink at the rate set by the pot on A0 |
| Mega2560Ports | a Mega 2560: a running light written to PORTA, the pot on A15 on Serial, steps on Serial1 (pick the port in the Monitor) |
| ProMini3v3 | a Pro Mini at 3.3 V and 8 MHz: F_CPU, the pot in volts |
| BuzzerMelody | tone() plays a scale on a buzzer: listen with sound on |
| LedCurrents | Ohm's law at work: LEDs through 220 Ω, 4.7 kΩ and no resistor, a voltage divider |
| LogicAnalyzerDemo | I2C, SPI, Serial and bit-banged traffic to look at on the Диаграмма tab |
