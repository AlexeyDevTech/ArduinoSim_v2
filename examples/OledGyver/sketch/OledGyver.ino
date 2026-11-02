// OLED GyverOLED: big text, a frame and a counter on a 128x64 SSD1306 over I2C,
// drawn with GyverOLED (AlexGyver) in buffered mode.
// Wiring: GND-GND, VCC-5V, SCL-A5, SDA-A4 (address 0x3C).

#include <GyverOLED.h>

GyverOLED<SSD1306_128x64, OLED_BUFFER> oled;

void setup() {
  oled.init();
  oled.clear();
  oled.setScale(2);
  oled.setCursorXY(16, 4);
  oled.print("GyverOLED");
  oled.setScale(1);
  oled.rect(0, 28, 127, 63, OLED_STROKE);
  oled.update();
}

void loop() {
  static int count = 0;
  oled.clear(2, 30, 125, 61);
  oled.setCursorXY(8, 36);
  oled.print("count: ");
  oled.print(count++);
  oled.circle(100 + (count % 3) * 6, 48, 4, OLED_FILL);
  oled.update();
  delay(250);
}