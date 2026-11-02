// LCD Hello: the LiquidCrystal HelloWorld on a 20x4 display wired in 4-bit mode,
// plus a custom glyph from createChar().
// Wiring: RS-D12, E-D11, D4-D5, D5-D4, D6-D3, D7-D2, RW and VSS to GND, VDD to 5V,
// backlight A to 5V and K to GND. V0 is not connected: the part's contrast property applies.

#include <LiquidCrystal.h>

const int rs = 12, en = 11, d4 = 5, d5 = 4, d6 = 3, d7 = 2;
LiquidCrystal lcd(rs, en, d4, d5, d6, d7);

byte heart[8] = {
  0b00000,
  0b01010,
  0b11111,
  0b11111,
  0b01110,
  0b00100,
  0b00000,
  0b00000,
};

void setup() {
  lcd.begin(20, 4);
  lcd.createChar(0, heart);
  lcd.setCursor(0, 0);  // back to DDRAM after writing the glyph to CGRAM
  lcd.print("hello, world!");
  lcd.setCursor(0, 2);
  lcd.print("20x4, 4-bit mode");
  lcd.setCursor(0, 3);
  lcd.print("I ");
  lcd.write(byte(0));
  lcd.print(" Arduino");
}

void loop() {
  // Seconds since reset on the second row.
  lcd.setCursor(0, 1);
  lcd.print(millis() / 1000);
}