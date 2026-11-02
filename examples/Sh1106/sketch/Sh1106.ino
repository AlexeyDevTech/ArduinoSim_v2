// SH1106: a 1.3" 128x64 SH1106 module over hardware SPI, drawn with Adafruit_SH110X.
// Wiring: D0 (SCK)-D13, D1 (MOSI)-D11, CS-D10, DC-D9, RES-D8, VCC-5V, GND-GND.
// The SH1106 has 132 RAM columns; the library shows columns 2 to 129.

#include <SPI.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SH110X.h>

Adafruit_SH1106G display(128, 64, &SPI, /* dc=*/ 9, /* reset=*/ 8, /* cs=*/ 10);

void setup() {
  display.begin(0, true);  // the address is not used over SPI
  display.setTextColor(SH110X_WHITE);
}

void loop() {
  unsigned long t = millis();
  display.clearDisplay();
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("SH1106 over SPI");
  display.drawRoundRect(0, 14, 128, 50, 6, SH110X_WHITE);
  display.fillRect(8, 30, (t / 20) % 112, 10, SH110X_WHITE);
  display.setCursor(8, 48);
  display.print(t / 1000.0, 1);
  display.print(" s");
  display.display();
  delay(30);
}
