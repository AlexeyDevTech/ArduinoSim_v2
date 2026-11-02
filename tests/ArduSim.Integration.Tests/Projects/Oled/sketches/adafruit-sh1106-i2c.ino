// Pattern for the OLED tests with Adafruit_SH110X on an SH1106 over I2C.
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SH110X.h>

Adafruit_SH1106G display(128, 64, &Wire, -1);

void setup() {
  display.begin(0x3C, true);
  display.clearDisplay();
  display.fillRect(0, 0, 16, 8, SH110X_WHITE);
  display.drawPixel(127, 63, SH110X_WHITE);
  display.display();
}

void loop() {}