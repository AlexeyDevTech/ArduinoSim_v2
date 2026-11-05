// UnoBlink: an Arduino Uno R3 blinks its own LED on D13 and one on D7, at a rate set by the
// potentiometer on A0. Turn the pot while it runs.

const int ledPin = 7;

void setup() {
  pinMode(LED_BUILTIN, OUTPUT);
  pinMode(ledPin, OUTPUT);
  Serial.begin(9600);
  Serial.println("Uno ready");
}

void loop() {
  int wait = map(analogRead(A0), 0, 1023, 50, 1000);
  digitalWrite(LED_BUILTIN, HIGH);
  digitalWrite(ledPin, LOW);
  delay(wait);
  digitalWrite(LED_BUILTIN, LOW);
  digitalWrite(ledPin, HIGH);
  delay(wait);
}