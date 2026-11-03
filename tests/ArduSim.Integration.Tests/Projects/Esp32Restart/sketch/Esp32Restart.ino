#include <Preferences.h>
#include <EEPROM.h>

Preferences prefs;

void setup() {
  Serial.begin(115200);
  prefs.begin("boot", false);
  unsigned int count = prefs.getUInt("count", 0) + 1;
  prefs.putUInt("count", count);
  prefs.putString("name", "ArduSim");
  prefs.putFloat("pi", 3.25f);
  prefs.end();
  EEPROM.begin(16);
  EEPROM.write(0, EEPROM.read(0) + 1);
  EEPROM.commit();
  Serial.printf("boot %u at %lu ms, eeprom %u\n", count, millis(), EEPROM.read(0));
  delay(1000);
  if (count % 3 != 0) ESP.restart();
}

void loop() {}