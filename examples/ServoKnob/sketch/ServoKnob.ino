// ServoKnob: the Servo library moves an SG90 on D9 to the angle of the potentiometer on A0.
// Drag the pot's knob while it runs; the servo's horn follows.

#include <Servo.h>

Servo servo;
int lastAngle = -1;

void setup() {
  servo.attach(9);
  Serial.begin(9600);
}

void loop() {
  int angle = map(analogRead(A0), 0, 1023, 0, 180);
  servo.write(angle);
  if (abs(angle - lastAngle) >= 5) {
    Serial.print("Angle: ");
    Serial.println(angle);
    lastAngle = angle;
  }
  delay(15);
}