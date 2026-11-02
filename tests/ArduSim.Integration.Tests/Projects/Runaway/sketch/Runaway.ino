// loop() never returns and never calls the Arduino API: the runner must notice and stop it.
volatile unsigned long spins = 0;

void setup() {
  Serial.begin(9600);
}

void loop() {
  while (true) {
    spins++;
  }
}
