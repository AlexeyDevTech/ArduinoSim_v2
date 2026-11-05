// Mega2560Ports: an Arduino Mega 2560 runs a light along port A (D22-D29, LEDs on the even pins)
// by writing PORTA directly. It reads the potentiometer on A15 and sends it to Serial, and the step
// count to Serial1: pick the port in the Monitor's list next to the baud rate.

void setup() {
  DDRA = 0xFF;
  Serial.begin(9600);
  Serial1.begin(9600);
  Serial.println("Mega ready");
}

void loop() {
  static uint8_t step = 0;
  PORTA = 1 << step;
  step = (step + 1) % 8;
  Serial.print("A15 = ");
  Serial.println(analogRead(A15));
  Serial1.print("step ");
  Serial1.println(step);
  delay(250);
}