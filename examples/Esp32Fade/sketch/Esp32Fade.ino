// ESP32 Fade: LEDC PWM (core 3.x API) breathes the LED on GPIO 4 at 5 kHz with 10-bit duty.
// Wiring: D4 - 220 Ω - LED - GND.

const int ledPin = 4;
const int maxDuty = (1 << 10) - 1;

void setup() {
  Serial.begin(115200);
  ledcAttach(ledPin, 5000, 10);
}

void loop() {
  for (int duty = 0; duty <= maxDuty; duty += 32) {
    ledcWrite(ledPin, duty);
    delay(10);
  }
  for (int duty = maxDuty; duty >= 0; duty -= 32) {
    ledcWrite(ledPin, duty);
    delay(10);
  }
  Serial.printf("breath done at %lu ms\n", millis());
}