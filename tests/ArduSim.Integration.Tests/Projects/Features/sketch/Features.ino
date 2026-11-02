// Exercises the build pipeline and the core API end to end: a static constructor that
// touches a pin before setup(), a function called before its definition with a default
// argument, EEPROM persisted across runs, String formatting and a floating input.
#include <EEPROM.h>

struct EarlyPin {
  EarlyPin(uint8_t pin) {
    pinMode(pin, OUTPUT);
    digitalWrite(pin, HIGH);
  }
};

EarlyPin early(5);

void setup() {
  Serial.begin(115200);
  report("boot", 3);

  byte runs = EEPROM.read(0);
  if (runs == 0xFF) {
    runs = 0;
  }
  EEPROM.write(0, runs + 1);
  Serial.print("runs=");
  Serial.println(runs + 1);

  pinMode(2, INPUT);
  digitalRead(2);
  digitalRead(2);

  String s = String("pi=") + String(3.14159, 3);
  Serial.println(s);
}

void loop() {
  Serial.print("t=");
  Serial.println(millis());
  delay(250);
}

void report(const char *what, int times = 1) {
  for (int i = 0; i < times; i++) {
    Serial.print(what);
  }
  Serial.println();
}
