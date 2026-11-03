// OledSsd1309: U8g2 on a 2.42" 128x64 SSD1309 OLED over hardware SPI (CS D10, DC D9, RES D8;
// D0 = SCK on D13, D1 = MOSI on D11). A frame, a title and a moving bar.

#include <U8g2lib.h>
#include <SPI.h>

U8G2_SSD1309_128X64_NONAME0_F_4W_HW_SPI u8g2(U8G2_R0, 10, 9, 8);

void setup() {
  u8g2.begin();
}

void loop() {
  static int x = 0;
  u8g2.clearBuffer();
  u8g2.drawFrame(0, 0, 128, 64);
  u8g2.setFont(u8g2_font_ncenB10_tr);
  u8g2.drawStr(14, 24, "SSD1309");
  u8g2.drawBox(x, 40, 24, 12);
  u8g2.sendBuffer();
  x = (x + 4) % 104;
  delay(40);
}