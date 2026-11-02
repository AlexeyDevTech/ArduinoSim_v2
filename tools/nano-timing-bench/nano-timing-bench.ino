// Measures how long core Arduino calls take on a real 16 MHz Nano, to
// calibrate ArduSim's timing table (docs/adr/ADR-005-virtual-time.md).
//
// Flash it with any Arduino IDE or arduino-cli, open the Serial Monitor at
// 115200 baud and copy the table it prints. Nothing needs to be wired; with an
// I2C device on A4/A5 (for example an OLED at 0x3C) the Wire line measures an
// acknowledged write, otherwise a NACKed one. Timer0's millis interrupt runs
// during the measurements and adds well under 1 %.
#include <Wire.h>

volatile unsigned long sink;
unsigned long baselineNs;

unsigned long nsPerCall(unsigned long elapsedUs, unsigned int n) {
  return elapsedUs * 1000UL / n;
}

#define MEASURE(label, n, stmt)                                    \
  do {                                                             \
    unsigned long t0 = micros();                                   \
    for (unsigned int i = 0; i < (n); i++) {                       \
      stmt;                                                        \
    }                                                              \
    unsigned long ns = nsPerCall(micros() - t0, (n));              \
    Serial.print(F(label));                                        \
    Serial.print(F("\t"));                                         \
    Serial.println(ns > baselineNs ? ns - baselineNs : 0);         \
  } while (0)

void setup() {
  Serial.begin(115200);
  pinMode(13, OUTPUT);
  pinMode(9, OUTPUT);
  pinMode(7, OUTPUT);
  pinMode(8, OUTPUT);
  pinMode(2, INPUT_PULLUP);
  delay(500);

  unsigned long t0 = micros();
  for (unsigned int i = 0; i < 4000; i++) {
    sink = i;
  }
  baselineNs = nsPerCall(micros() - t0, 4000);

  Serial.println(F("call\tns_per_call"));
  MEASURE("digitalWrite(13)", 4000, digitalWrite(13, i & 1));
  MEASURE("digitalWrite(9,pwm)", 4000, digitalWrite(9, i & 1));
  MEASURE("digitalRead(2)", 4000, sink = digitalRead(2));
  MEASURE("pinMode(4)", 4000, pinMode(4, INPUT));
  MEASURE("analogWrite(9)", 4000, analogWrite(9, i & 255));
  MEASURE("analogRead(A0)", 400, sink = analogRead(A0));
  MEASURE("millis()", 4000, sink = millis());
  MEASURE("micros()", 4000, sink = micros());
  MEASURE("shiftOut(byte)", 1000, shiftOut(7, 8, MSBFIRST, i));

  // Serial: once the 64-byte TX buffer is full, each byte costs one frame (10 bits).
  Serial.flush();
  t0 = micros();
  for (unsigned int i = 0; i < 640; i++) {
    Serial.write('.');
  }
  unsigned long serialUs = micros() - t0;
  Serial.println();
  Serial.print(F("Serial.write x640 @115200 (us)\t"));
  Serial.println(serialUs);

  Wire.begin();
  for (int k = 0; k < 2; k++) {
    unsigned long clock = k == 0 ? 100000UL : 400000UL;
    Wire.setClock(clock);
    t0 = micros();
    uint8_t result = 0;
    for (unsigned int i = 0; i < 100; i++) {
      Wire.beginTransmission(0x3C);
      Wire.write(0x00);
      Wire.write(0xAF);
      result = Wire.endTransmission();
    }
    Serial.print(F("Wire 2-byte write @"));
    Serial.print(clock);
    Serial.print(F(" result="));
    Serial.print(result);
    Serial.print(F(" (ns)\t"));
    Serial.println(nsPerCall(micros() - t0, 100));
  }
  Serial.println(F("done"));
}

void loop() {}
