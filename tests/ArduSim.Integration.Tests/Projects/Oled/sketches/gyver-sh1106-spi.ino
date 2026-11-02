// Pattern for the OLED tests: a 16x8 box at the top-left corner and one pixel at (127, 63).
#include <GyverOLED.h>

GyverOLED<SSH1106_128x64, OLED_BUFFER, OLED_SPI, 10, 9, 8> oled;

void setup() {
  oled.init();
  oled.clear();
  oled.rect(0, 0, 15, 7, OLED_FILL);
  oled.dot(127, 63);
  oled.update();
}

void loop() {}