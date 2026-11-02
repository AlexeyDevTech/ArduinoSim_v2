// Potentiometer: turn the knob (select the part, then drag its knob or edit "value").
// analogRead(A0) goes to the Serial Plotter (Плоттер tab) and dims the LED on D9 with analogWrite.
// Wiring: pot ends to GND and 5V, wiper to A0; D9 - LED - 220 Ω - GND.

const int potPin = A0;
const int ledPin = 9;  // a PWM pin

void setup() {
  Serial.begin(115200);
  pinMode(ledPin, OUTPUT);
}

void loop() {
  int value = analogRead(potPin);  // 0..1023
  analogWrite(ledPin, value / 4);  // 0..255
  Serial.print("pot:");
  Serial.print(value);
  Serial.print(" led:");
  Serial.println(value / 4);
  delay(20);
}