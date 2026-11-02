// Pattern for the OLED tests over hardware SPI with Adafruit_SSD1306.
#include <SPI.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

Adafruit_SSD1306 display(128, 64, &SPI, /* dc=*/ 9, /* reset=*/ 8, /* cs=*/ 10);

void setup() {
  display.begin(SSD1306_SWITCHCAPVCC);
  display.clearDisplay();
  display.fillRect(0, 0, 16, 8, SSD1306_WHITE);
  display.drawPixel(127, 63, SSD1306_WHITE);
  display.display();
}

void loop() {}