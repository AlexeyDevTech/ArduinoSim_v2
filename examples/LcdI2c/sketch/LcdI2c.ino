// LCD I2C: "hello, world!" and the seconds since reset on a 16x2 LCD with a PCF8574
// backpack at address 0x27, driven by LiquidCrystal_I2C (Frank de Brabander).
// Wiring: GND-GND, VCC-5V, SDA-A4, SCL-A5. The library's init() waits 1 s before
// it talks to the display, so the text appears about a second after the start.

#include <Wire.h>
#include <LiquidCrystal_I2C.h>

LiquidCrystal_I2C lcd(0x27, 16, 2);

void setup() {
  lcd.init();
  lcd.backlight();
  lcd.print("hello, world!");
}

void loop() {
  lcd.setCursor(0, 1);
  lcd.print(millis() / 1000);
}