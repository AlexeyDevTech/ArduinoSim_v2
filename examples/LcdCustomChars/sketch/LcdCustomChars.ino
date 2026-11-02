// LCD Custom Chars: three glyphs made with createChar(), then the whole display
// scrolls left with scrollDisplayLeft() every 400 ms. Both rows move together, and
// text beyond column 16 comes into view, because the HD44780 keeps 40 characters per row.
// Wiring: GND-GND, VCC-5V, SDA-A4, SCL-A5 (PCF8574 backpack at 0x27).

#include <Wire.h>
#include <LiquidCrystal_I2C.h>

LiquidCrystal_I2C lcd(0x27, 16, 2);

byte heart[8] = {0x00, 0x0A, 0x1F, 0x1F, 0x0E, 0x04, 0x00, 0x00};
byte smile[8] = {0x00, 0x0A, 0x0A, 0x00, 0x11, 0x0E, 0x00, 0x00};
byte bell[8] = {0x04, 0x0E, 0x0E, 0x0E, 0x1F, 0x00, 0x04, 0x00};

void setup() {
  lcd.init();
  lcd.backlight();
  lcd.createChar(0, heart);
  lcd.createChar(1, smile);
  lcd.createChar(2, bell);
  lcd.home();  // back to DDRAM after writing the glyphs to CGRAM
  lcd.print("Glyphs: ");
  lcd.write(byte(0));
  lcd.write(byte(1));
  lcd.write(byte(2));
  lcd.setCursor(0, 1);
  lcd.print("scrollDisplayLeft() moves both rows");
}

void loop() {
  delay(400);
  lcd.scrollDisplayLeft();
}