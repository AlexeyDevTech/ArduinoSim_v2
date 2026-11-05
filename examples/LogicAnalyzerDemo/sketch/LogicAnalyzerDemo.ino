// LogicAnalyzerDemo: something to look at on the Диаграмма tab. Every half second the Nano reads the
// seconds from a DS3231 over I2C, draws them on a MAX7219 over hardware SPI, reports them on Serial,
// shifts the same byte out by hand on D4/D3 (bit-banged SPI: D3 clock, D4 data) and dims an LED on D5.
// Open Диаграмма, then + Шина: I2C, SPI, Serial TX, and Другие выводы: SPI, MOSI D4, SCK D3 for the
// bit-banged byte; + Сигнал: D3, D4, D5, D10. Press Run; zoom with the wheel, drag to pan, click to
// set the marker that Δ measures from.

#include <SPI.h>
#include <Wire.h>

void maxWrite(byte address, byte value) {
  digitalWrite(10, LOW);
  SPI.transfer(address);
  SPI.transfer(value);
  digitalWrite(10, HIGH);
}

byte readSeconds() {
  Wire.beginTransmission(0x68);
  Wire.write(0);
  Wire.endTransmission();
  Wire.requestFrom(0x68, 1);
  return Wire.read();
}

void setup() {
  Serial.begin(115200);
  Wire.begin();
  SPI.begin();
  pinMode(10, OUTPUT);
  pinMode(3, OUTPUT);
  pinMode(4, OUTPUT);
  maxWrite(0x0F, 0);  // display test off
  maxWrite(0x0B, 7);  // scan all rows
  maxWrite(0x09, 0);  // no decoding
  maxWrite(0x0A, 3);  // intensity
  maxWrite(0x0C, 1);  // out of shutdown
}

void loop() {
  byte seconds = readSeconds();
  maxWrite(1, seconds);
  Serial.println(seconds, HEX);
  shiftOut(4, 3, MSBFIRST, seconds);
  analogWrite(5, seconds * 2);
  delay(500);
}