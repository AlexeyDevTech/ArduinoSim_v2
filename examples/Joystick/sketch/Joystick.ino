// Joystick: an analog stick on A0 (X) and A1 (Y) with its button on D2.
// Drag the cap (it springs back when you let go); click it without moving to press.
// The values go to the Serial Plotter tab as x and y.

const int xPin = A0;
const int yPin = A1;
const int buttonPin = 2;

void setup() {
  pinMode(buttonPin, INPUT_PULLUP);
  Serial.begin(9600);
}

void loop() {
  int x = analogRead(xPin);
  int y = analogRead(yPin);
  Serial.print("x:");
  Serial.print(x);
  Serial.print(" y:");
  Serial.print(y);
  if (digitalRead(buttonPin) == LOW) {
    Serial.print(" pressed");
  }
  Serial.println();
  delay(100);
}