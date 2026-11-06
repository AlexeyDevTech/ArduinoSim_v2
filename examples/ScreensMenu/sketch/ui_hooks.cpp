// Your part of the screens: what they show and what their menu items do.
// ArduSim only ever adds to this file; what you write here stays.
#include "ui_screens.h"

// The value "uptime" on a screen: write at most size - 1 characters to out.
void uiValue_uptime(char* out, uint8_t size) {
  snprintf(out, size, "%lu s", millis() / 1000);
}

// The value "adc" on a screen.
void uiValue_adc(char* out, uint8_t size) {
  snprintf(out, size, "%d", analogRead(A0));
}
