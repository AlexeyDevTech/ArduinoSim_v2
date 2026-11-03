// ESP32 Blink: the on-board blue LED (GPIO 2) and an LED on GPIO 4 blink in turn.
// Wiring: D4 - 220 Ω - LED - GND. Arduino pin numbers on the ESP32 are GPIO numbers.

const int ledPin = 4;

void setup() {
  Serial.begin(115200);
  pinMode(LED_BUILTIN, OUTPUT);
  pinMode(ledPin, OUTPUT);
  Serial.printf("ESP32 Blink on a %s at %u MHz\n", ESP.getChipModel(), getCpuFrequencyMhz());
}

void loop() {
  digitalWrite(LED_BUILTIN, HIGH);
  digitalWrite(ledPin, LOW);
  delay(500);
  digitalWrite(LED_BUILTIN, LOW);
  digitalWrite(ledPin, HIGH);
  delay(500);
}