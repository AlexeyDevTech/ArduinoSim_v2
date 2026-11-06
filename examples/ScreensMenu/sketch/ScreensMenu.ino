// Screens and transitions drawn on the Экраны tab: a 20x4 LCD on I2C, a rotary encoder that
// moves through a menu and enters what it points at, a button that goes home from any screen.
// ArduSim generates ui_screens.h and ui_screens.cpp from screens.json before each build;
// ui_hooks.cpp is yours: what the screens' values show.

#include "ui_screens.h"

void setup() {
  uiBegin();
}

void loop() {
  uiUpdate();
}
