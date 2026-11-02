// Serial TX timing at 9600 baud: 1 byte shifting + 64 in the ring are accepted at once,
// every further byte waits for a frame (10 bits = 1041.7 us) to finish.

void setup() {
  Serial.begin(9600);
  unsigned long start = micros();
  for (int i = 0; i < 100; i++) {
    Serial.write('x');
  }
  unsigned long queued = micros() - start;
  Serial.flush();
  unsigned long sent = micros() - start;
  Serial.println();
  Serial.print("queued_us=");
  Serial.println(queued);
  Serial.print("sent_us=");
  Serial.println(sent);
}

void loop() {}
