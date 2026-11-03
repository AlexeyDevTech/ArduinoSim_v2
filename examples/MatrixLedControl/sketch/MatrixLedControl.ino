// MatrixLedControl: the LedControl library draws on one MAX7219 8x8 module
// (DIN on D12, CLK on D11, CS on D10): a heart that beats.

#include <LedControl.h>

LedControl lc(12, 11, 10, 1);

const byte big[8] = {0b00000000, 0b01100110, 0b11111111, 0b11111111, 0b11111111, 0b01111110, 0b00111100, 0b00011000};
const byte small[8] = {0b00000000, 0b00000000, 0b00100100, 0b01111110, 0b01111110, 0b00111100, 0b00011000, 0b00000000};

void show(const byte rows[8]) {
  for (int row = 0; row < 8; row++) {
    lc.setRow(0, row, rows[row]);
  }
}

void setup() {
  lc.shutdown(0, false);  // the MAX7219 starts in shutdown
  lc.setIntensity(0, 8);
  lc.clearDisplay(0);
}

void loop() {
  show(big);
  delay(400);
  show(small);
  delay(400);
}