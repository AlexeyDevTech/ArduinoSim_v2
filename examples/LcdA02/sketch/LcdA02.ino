// LcdA02: a 1602 LCD whose HD44780 has the European character ROM (A02), over an I2C backpack
// at 0x27. A02 has Latin-1 letters at their ISO 8859-1 codes and the Cyrillic capitals that
// differ from Latin ones at 0x80-0x8F, enough for "ПРИВЕТ, МИР!" in capitals. Set the part's
// character ROM to A00 to see what a Japanese-ROM display makes of the same bytes.

#include <Wire.h>
#include <LiquidCrystal_I2C.h>

LiquidCrystal_I2C lcd(0x27, 16, 2);

void setup() {
  lcd.init();
  lcd.backlight();
  // П Р И В Е Т ,   М И Р !   (П = 0x87, И = 0x84; Р, В, Е, Т, М as their Latin twins)
  lcd.print("\x87" "P" "\x84" "BET, M" "\x84" "P!");
  lcd.setCursor(0, 1);
  // 23.5 °C  Ä Ö Ü ß (Latin-1: ° 0xB0, Ä 0xC4, Ö 0xD6, Ü 0xDC, ß 0xDF)
  lcd.print("23.5\xB0" "C  \xC4\xD6\xDC\xDF");
}

void loop() {
}