// Tm1637: a TM1637 four-digit module (CLK on D2, DIO on D3) as a minutes:seconds timer
// with a blinking colon, through the TM1637Display library.

#include <TM1637Display.h>

TM1637Display display(2, 3);

void setup() {
  display.setBrightness(5);
  display.clear();
}

void loop() {
  unsigned long seconds = millis() / 1000;
  int shown = (seconds / 60) * 100 + seconds % 60;
  bool colon = (millis() / 500) % 2 == 0;
  display.showNumberDecEx(shown, colon ? 0b01000000 : 0, true);
  delay(100);
}