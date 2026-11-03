// TftIli9341: Adafruit_ILI9341 on a 2.8" 240x320 TFT over hardware SPI (CS D10, DC D9, RESET D8,
// SDO on D12 for the status reads). It prints the controller's status, draws a test card and then
// counts seconds in a box.

#include <SPI.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ILI9341.h>

Adafruit_ILI9341 tft(10, 9, 8);

void setup() {
  Serial.begin(9600);
  tft.begin();
  Serial.print("Display power mode: 0x");
  Serial.println(tft.readcommand8(ILI9341_RDMODE), HEX);
  Serial.print("Pixel format: 0x");
  Serial.println(tft.readcommand8(ILI9341_RDPIXFMT), HEX);

  tft.fillScreen(ILI9341_BLACK);
  const uint16_t bars[] = {ILI9341_WHITE, ILI9341_YELLOW, ILI9341_CYAN, ILI9341_GREEN, ILI9341_MAGENTA, ILI9341_RED, ILI9341_BLUE, ILI9341_BLACK};
  for (int i = 0; i < 8; i++) {
    tft.fillRect(i * 30, 0, 30, 120, bars[i]);
  }
  tft.drawRect(10, 140, 220, 60, ILI9341_WHITE);
  tft.fillCircle(60, 260, 40, ILI9341_ORANGE);
  tft.drawCircle(180, 260, 40, ILI9341_GREENYELLOW);
  tft.setTextColor(ILI9341_WHITE);
  tft.setTextSize(2);
  tft.setCursor(20, 150);
  tft.print("ArduSim");
  Serial.println("Test card drawn");
}

void loop() {
  static unsigned long last = 0;
  if (millis() - last >= 1000) {
    last = millis();
    tft.fillRect(20, 172, 200, 20, ILI9341_BLACK);
    tft.setCursor(20, 172);
    tft.setTextColor(ILI9341_YELLOW);
    tft.print(last / 1000);
    tft.print(" s");
  }
}