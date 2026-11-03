// ESP8266 OLED on a Wemos D1 mini: an SSD1306 128x64 on D2 (SDA, GPIO 4) and D1 (SCL, GPIO 5),
// drawn with U8g2. Wiring: GND-GND, VCC-3V3, SCL-D1, SDA-D2 (address 0x3C).

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
  u8g2.drawStr(0, 12, "ESP8266 D1 mini");
  snprintf(line, sizeof line, "up %lu s", millis() / 1000);
  u8g2.drawStr(0, 32, line);
  snprintf(line, sizeof line, "heap %u", ESP.getFreeHeap());
  u8g2.drawStr(0, 50, line);
  u8g2.drawFrame(0, 0, 128, 64);
  u8g2.sendBuffer();
  delay(200);
}