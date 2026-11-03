// RotaryEncoder: a KY-040 module on D4 (CLK), D3 (DT) and D2 (SW).
// Turn the knob with the mouse wheel or by dragging around it; click it to press the shaft.
// Clockwise, CLK falls first while DT is still HIGH, so the count goes up.

const int clkPin = 4;
const int dtPin = 3;
const int swPin = 2;

int position = 0;
int lastClk = HIGH;
int lastButton = HIGH;

void setup() {
  pinMode(clkPin, INPUT);        // the module has its own pull-ups on CLK and DT
  pinMode(dtPin, INPUT);
  pinMode(swPin, INPUT_PULLUP);  // but none on SW
  Serial.begin(9600);
  Serial.println("Turn the knob.");
}

void loop() {
  int clk = digitalRead(clkPin);
  if (clk != lastClk && clk == LOW) {
    position += digitalRead(dtPin) == HIGH ? 1 : -1;
    Serial.print("Position: ");
    Serial.println(position);
  }
  lastClk = clk;

  int button = digitalRead(swPin);
  if (button != lastButton) {
    if (button == LOW) {
      Serial.println("Button pressed, position reset");
      position = 0;
    }
    lastButton = button;
    delay(5);  // debounce
  }
}