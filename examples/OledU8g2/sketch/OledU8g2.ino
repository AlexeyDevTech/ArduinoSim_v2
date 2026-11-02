// OLED U8g2: text, a frame, a bouncing ball and a seconds counter on a 128x64 SSD1306
// over I2C, drawn with U8g2 in full-buffer mode.
// Wiring: GND-GND, VCC-5V, SCL-A5, SDA-A4 (address 0x3C).

#include <Arduino.h>
#include <U8g2lib.h>
#include <Wire.h>

U8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);

int ballX = 10;
int step = 2;

void setup() {
  u8g2.begin();
}

void loop() {
  u8g2.clearBuffer();
  u8g2.setFont(u8g2_font_ncenB08_tr);
  u8g2.drawStr(0, 10, "Hello from U8g2!");
  u8g2.drawFrame(0, 16, 128, 48);
  u8g2.drawDisc(ballX, 40, 6);
  u8g2.setCursor(84, 60);
  u8g2.print(millis() / 1000);
  u8g2.print(" s");
  u8g2.sendBuffer();

  ballX += step;
  if (ballX < 8 || ballX > 119) {
    step = -step;
  }
  delay(20);
}