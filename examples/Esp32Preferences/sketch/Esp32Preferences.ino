// ESP32 Preferences: a boot counter in NVS. Every boot adds one and the sketch restarts itself
// every 3 seconds; Stop and Run again and the count goes on (it lives in the project's
// preferences.json, as NVS lives in flash on the board).

#include <Preferences.h>

Preferences prefs;

void setup() {
  Serial.begin(115200);
  prefs.begin("demo", false);
  unsigned int boots = prefs.getUInt("boots", 0) + 1;
  prefs.putUInt("boots", boots);
  prefs.end();
  Serial.printf("Boot number %u\n", boots);
}

void loop() {
  if (millis() > 3000) {
    Serial.println("Restarting...");
    ESP.restart();
  }
  delay(100);
}