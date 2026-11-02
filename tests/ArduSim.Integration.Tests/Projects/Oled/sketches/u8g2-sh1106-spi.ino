// Pattern for the OLED tests: a 16x8 box at the top-left corner and one pixel at (127, 63).
#include <Arduino.h>
#include <U8g2lib.h>
#include <SPI.h>

U8G2_SH1106_128X64_NONAME_F_4W_HW_SPI u8g2(U8G2_R0, 10, 9, 8);

void setup() {
  u8g2.begin();
  u8g2.clearBuffer();
  u8g2.drawBox(0, 0, 16, 8);
  u8g2.drawPixel(127, 63);
  u8g2.sendBuffer();
}

void loop() {}