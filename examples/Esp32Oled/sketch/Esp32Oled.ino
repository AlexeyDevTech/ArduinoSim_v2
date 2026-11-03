// ESP32 OLED: an SSD1306 128x64 on the ESP32's default I2C pins (SDA 21, SCL 22), drawn with U8g2:
// uptime and the free heap. Wiring: GND-GND, VCC-3V3, SCL-D22, SDA-D21 (address 0x3C).

#include <Wire.h>
#include <U8g2lib.h>

U8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);

void setup() {
  u8g2.begin();
}

void loop() {
  char line[24];
  u8g2.clearBuffer();
  u8g2.setFont(u8g2_font_ncenB08_tr);
  u8g2.drawStr(0, 12, "ESP32 + SSD1306");
  snprintf(line, sizeof line, "up %lu s", millis() / 1000);
  u8g2.drawStr(0, 32, line);
  snprintf(line, sizeof line, "heap %u", ESP.getFreeHeap());
  u8g2.drawStr(0, 50, line);
  u8g2.drawFrame(0, 0, 128, 64);
  u8g2.sendBuffer();
  delay(200);
}