// TftSt7735: a 1.8" 128x160 ST7735 module (the "black tab" kind) with Adafruit_ST7735,
// on hardware SPI (CS D10, A0/DC D9, RESET D8). It shows the potentiometer on A0 as a number and a bar.
// Turn the pot while it runs.

#include <SPI.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ST7735.h>

Adafruit_ST7735 tft(10, 9, 8);
int shown = -1;

void setup() {
  Serial.begin(9600);
  tft.initR(INITR_BLACKTAB);
  tft.setRotation(0); // portrait; 1 and 3 turn the picture a quarter for a module mounted on its side
  tft.fillScreen(ST77XX_BLACK);
  tft.setTextColor(ST77XX_CYAN);
  tft.setTextSize(1);
  tft.setCursor(4, 8);
  tft.print("Potentiometer on A0");
  tft.drawRect(4, 100, 120, 20, ST77XX_WHITE);
}

void loop() {
  int value = analogRead(A0);
  if (abs(value - shown) >= 8) {
    shown = value;
    int width = map(value, 0, 1023, 0, 116);
    tft.fillRect(6, 102, width, 16, ST77XX_GREEN);
    tft.fillRect(6 + width, 102, 116 - width, 16, ST77XX_BLACK);
    tft.fillRect(4, 40, 120, 32, ST77XX_BLACK);
    tft.setTextSize(4);
    tft.setTextColor(ST77XX_YELLOW);
    tft.setCursor(4, 40);
    tft.print(value);
    Serial.print("A0 = ");
    Serial.println(value);
  }
  delay(50);
}