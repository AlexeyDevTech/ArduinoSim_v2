// Button: each press of the button on D2 toggles the LED on D8.
// The button pulls D2 to GND; INPUT_PULLUP keeps D2 HIGH while it is released.

const int buttonPin = 2;
const int ledPin = 8;
const unsigned long debounceMs = 20;

bool ledOn = false;
int lastReading = HIGH;
int stableState = HIGH;
unsigned long lastChange = 0;

void setup() {
  pinMode(buttonPin, INPUT_PULLUP);
  pinMode(ledPin, OUTPUT);
  Serial.begin(9600);
  Serial.println("Press the button to toggle the LED.");
}

void loop() {
  int reading = digitalRead(buttonPin);
  if (reading != lastReading) {
    lastChange = millis();
    lastReading = reading;
  }

  if (millis() - lastChange > debounceMs && reading != stableState) {
    stableState = reading;
    if (stableState == LOW) {
      ledOn = !ledOn;
      digitalWrite(ledPin, ledOn ? HIGH : LOW);
      Serial.println(ledOn ? "LED on" : "LED off");
    }
  }
}
