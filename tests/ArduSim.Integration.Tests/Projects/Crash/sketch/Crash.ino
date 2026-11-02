// Dereferences a null pointer in loop(): only the Runner may die, and the crash record must
// point at the faulting line.
int *target = nullptr;

void setup() {
  pinMode(13, OUTPUT);
}

void loop() {
  digitalWrite(13, HIGH);
  delay(10);
  *target = 42;
}
