// ESP8266 Fade on a Wemos D1 mini: software PWM on D2 (GPIO 4) with analogWriteRange(1023) at 1 kHz.
// Wiring: D2 - 220 Ω - LED - GND.

const int ledPin = D2;

void setup() {
  Serial.begin(115200);
  analogWriteRange(1023);
  analogWriteFreq(1000);
}

void loop() {
  for (int duty = 0; duty <= 1023; duty += 31) {
    analogWrite(ledPin, duty);
    delay(10);
  }
  for (int duty = 1023; duty >= 0; duty -= 31) {
    analogWrite(ledPin, duty);
    delay(10);
  }
  Serial.printf("breath done at %lu ms\n", millis());
}