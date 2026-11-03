// ESP8266 Blink on a NodeMCU v3: the module's blue LED (GPIO 2, lit while the pin is LOW) and an
// LED on D6 (GPIO 12) blink in turn. Wiring: D6 - 220 Ω - LED - GND.

const int ledPin = D6;

void setup() {
  Serial.begin(115200);
  pinMode(LED_BUILTIN, OUTPUT);
  pinMode(ledPin, OUTPUT);
  Serial.printf("ESP8266 Blink, chip %06X, reset reason: %s\n", ESP.getChipId(), ESP.getResetReason().c_str());
}

void loop() {
  digitalWrite(LED_BUILTIN, LOW);   // on
  digitalWrite(ledPin, LOW);
  delay(500);
  digitalWrite(LED_BUILTIN, HIGH);  // off
  digitalWrite(ledPin, HIGH);
  delay(500);
}