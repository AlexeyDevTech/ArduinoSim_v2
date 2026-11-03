// SdCardLog: the SD library on a microSD module (hardware SPI, CS on D10). It reads
// config.txt from the card, then appends the potentiometer's reading to log.csv every second.
// The card's files are the project's sd folder: look there after Stop (Properties: open the folder).

#include <SD.h>

const int chipSelect = 10;
unsigned long interval = 1000;

void setup() {
  Serial.begin(9600);
  if (!SD.begin(chipSelect)) {
    Serial.println("Card failed or not present");
    while (true) delay(10);
  }
  Serial.println("Card ready");

  File config = SD.open("config.txt");
  if (config) {
    String line = config.readStringUntil('\n');
    config.close();
    if (line.startsWith("interval=")) {
      interval = line.substring(9).toInt();
    }
  }
  Serial.print("Logging every ");
  Serial.print(interval);
  Serial.println(" ms");
}

void loop() {
  int value = analogRead(A0);
  File log = SD.open("log.csv", FILE_WRITE);
  if (log) {
    log.print(millis());
    log.print(',');
    log.println(value);
    log.close();
    Serial.print("Logged ");
    Serial.println(value);
  } else {
    Serial.println("Cannot open log.csv");
  }
  delay(interval);
}