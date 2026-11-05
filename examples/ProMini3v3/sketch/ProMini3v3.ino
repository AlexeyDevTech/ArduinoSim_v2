// ProMini3v3: an Arduino Pro Mini at 3.3 V and 8 MHz. Every Arduino call takes about twice as long
// as at 16 MHz; analogRead still gives 0-1023 over the 3.3 V supply. Turn the pot on A0 and see the
// volts in the Serial Monitor. The board's LED on D13 blinks once a second.

void setup() {
  pinMode(LED_BUILTIN, OUTPUT);
  Serial.begin(9600);
  Serial.print("F_CPU = ");
  Serial.println(F_CPU);
}

void loop() {
  digitalWrite(LED_BUILTIN, !digitalRead(LED_BUILTIN));
  Serial.print(analogRead(A0) * 3.3 / 1023, 2);
  Serial.println(" V");
  delay(500);
}