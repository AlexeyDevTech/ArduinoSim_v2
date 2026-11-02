// Host-call throughput (brief §6: at least 2 M calls per second): a tight loop of the cheapest
// calls. millis() and digitalRead() cost 1 and 4 µs of virtual time, so a run of T ms makes
// about T * 1000 / 5 * 2 calls; the test divides that by the wall time.

void setup() {
  pinMode(2, INPUT_PULLUP);
}

void loop() {
  for (int i = 0; i < 1000; i++) {
    (void)millis();
    (void)digitalRead(2);
  }
}