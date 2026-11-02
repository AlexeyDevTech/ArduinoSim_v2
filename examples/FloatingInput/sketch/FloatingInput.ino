// FloatingInput: the button connects D2 to 5V, but nothing pulls D2 down while the button
// is released, so the input floats and reads random values. The simulator warns about it.
// Fix: a 10k resistor from D2 to GND, or INPUT_PULLUP with the button wired to GND instead.

void setup() {
  Serial.begin(9600);
  pinMode(2, INPUT);
}

void loop() {
  Serial.println(digitalRead(2));
  delay(500);
}
