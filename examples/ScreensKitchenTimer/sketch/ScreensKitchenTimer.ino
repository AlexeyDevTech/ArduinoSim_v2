// A kitchen timer made on the Экраны tab, with no code of its own: a 20x4 LCD on I2C, a 4x4 keypad, a
// buzzer. Its clock, countdown, stopwatch, settings kept in EEPROM, Russian text and the notification
// that takes over any screen are all in screens.json. ArduSim generates ui_screens.h and ui_screens.cpp
// from it before each build; ui_hooks.cpp would be yours, and this project needs nothing in it.

#include "ui_screens.h"

void setup() {
  uiBegin();
}

void loop() {
  uiUpdate();
}
