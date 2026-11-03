// SevenSegment: the SevSeg library multiplexes a 4-digit common-cathode display
// (digits on D2-D5, segments a-g and dp on D6-D13) and counts tenths of a second.

#include <SevSeg.h>

SevSeg sevseg;
unsigned long lastStep = 0;
int tenths = 0;

void setup() {
  byte digitPins[] = {2, 3, 4, 5};
  byte segmentPins[] = {6, 7, 8, 9, 10, 11, 12, 13};
  sevseg.begin(COMMON_CATHODE, 4, digitPins, segmentPins, false);
  sevseg.setBrightness(90);
}

void loop() {
  if (millis() - lastStep >= 100) {
    lastStep += 100;
    tenths++;
    sevseg.setNumber(tenths, 1);
  }
  sevseg.refreshDisplay();
}