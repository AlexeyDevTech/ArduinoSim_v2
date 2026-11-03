// Relay: a 5 V relay module on D7 switches an LED on and off once a second.
// The module is low-level triggered: LOW on IN pulls the relay in.

const int relayPin = 7;

void setup() {
  pinMode(relayPin, OUTPUT);
  digitalWrite(relayPin, HIGH);  // off
  Serial.begin(9600);
}

void loop() {
  digitalWrite(relayPin, LOW);
  Serial.println("Relay on");
  delay(1000);
  digitalWrite(relayPin, HIGH);
  Serial.println("Relay off");
  delay(1000);
}