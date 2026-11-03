// TftSt7789Esp32: a 1.3" 240x240 IPS ST7789 without a CS pin on the ESP32's VSPI (SCL 18, SDA 23),
// DC on 2, RES on 4, with Adafruit_ST7789. Modules without CS want SPI mode 3. The backlight pin
// is pulled up on the module. Colored rings, then a dot that runs between two of them.

#include <SPI.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ST7789.h>

Adafruit_ST7789 tft(-1, 2, 4);

void setup() {
  Serial.begin(115200);
  tft.init(240, 240, SPI_MODE3);
  tft.fillScreen(ST77XX_BLACK);
  const uint16_t colors[] = {ST77XX_RED, ST77XX_ORANGE, ST77XX_YELLOW, ST77XX_GREEN, ST77XX_CYAN, ST77XX_BLUE, ST77XX_MAGENTA};
  for (int i = 0; i < 7; i++) {
    tft.drawCircle(120, 120, 110 - i * 12, colors[i]);
    tft.drawCircle(120, 120, 109 - i * 12, colors[i]);
  }
  tft.setTextColor(ST77XX_WHITE);
  tft.setTextSize(2);
  tft.setCursor(84, 112);
  tft.print("ST7789");
  Serial.println("Rings drawn");
}

void loop() {
  static int angle = 0;
  int x = 120 + 44 * cos(angle * PI / 180);
  int y = 120 + 44 * sin(angle * PI / 180);
  tft.fillCircle(x, y, 4, ST77XX_BLACK);
  angle = (angle + 10) % 360;
  x = 120 + 44 * cos(angle * PI / 180);
  y = 120 + 44 * sin(angle * PI / 180);
  tft.fillCircle(x, y, 4, ST77XX_WHITE);
  delay(30);
}