// Ultrasonic: an HC-SR04 with TRIG on D9 and ECHO on D10. The on-board LED (D13) lights when
// something is closer than 20 cm. Change the distance in Properties or drag the sensor.

const int trigPin = 9;
const int echoPin = 10;

void setup() {
  pinMode(trigPin, OUTPUT);
  pinMode(echoPin, INPUT);
  pinMode(LED_BUILTIN, OUTPUT);
  Serial.begin(9600);
}

void loop() {
  digitalWrite(trigPin, LOW);
  delayMicroseconds(2);
  digitalWrite(trigPin, HIGH);
  delayMicroseconds(10);
  digitalWrite(trigPin, LOW);

  unsigned long duration = pulseIn(echoPin, HIGH, 30000UL);
  if (duration == 0) {
    Serial.println("Out of range");
    digitalWrite(LED_BUILTIN, LOW);
  } else {
    float distance = duration * 0.0343 / 2;  // speed of sound, there and back
    Serial.print("Distance: ");
    Serial.print(distance, 1);
    Serial.println(" cm");
    digitalWrite(LED_BUILTIN, distance < 20 ? HIGH : LOW);
  }
  delay(200);
}