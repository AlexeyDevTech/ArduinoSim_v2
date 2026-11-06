// What Ohm's law does to LEDs: the same pin voltage through 220 ohms and through 4.7 kilohms.
// Hover over a pin or a wire to read its voltage and current. Hold the button to drive the third
// LED with no resistor at all: the pin is overloaded and the log says so.

const int bright = 10;
const int dim = 9;
const int unprotected = 6;
const int button = 2;

void setup() {
  Serial.begin(115200);
  pinMode(bright, OUTPUT);
  pinMode(dim, OUTPUT);
  pinMode(unprotected, OUTPUT);
  pinMode(button, INPUT_PULLUP);
  digitalWrite(bright, HIGH);
  digitalWrite(dim, HIGH);
}

void loop() {
  digitalWrite(unprotected, digitalRead(button) == LOW ? HIGH : LOW);

  // A0 sits on a divider of 10 k and 4.7 k: 5 V * 4.7 / 14.7 = 1.60 V.
  float volts = analogRead(A0) * 5.0 / 1023;
  Serial.print("Divider: ");
  Serial.print(volts);
  Serial.println(" V");
  delay(500);
}
