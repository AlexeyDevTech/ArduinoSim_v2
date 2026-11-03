// ESP8266 EEPROM on a Wemos D1 mini: the core emulates EEPROM in flash, so writes need
// EEPROM.begin(size) and EEPROM.commit(). A boot counter that survives ESP.restart() and runs.

#include <EEPROM.h>

void setup() {
  Serial.begin(115200);
  EEPROM.begin(16);
  uint8_t boots = EEPROM.read(0) == 0xFF ? 1 : EEPROM.read(0) + 1;
  EEPROM.write(0, boots);
  EEPROM.commit();
  Serial.printf("Boot %u (%s)\n", boots, ESP.getResetReason().c_str());
}

void loop() {
  if (millis() > 3000) {
    Serial.println("Restarting...");
    ESP.restart();
  }
  delay(100);
}