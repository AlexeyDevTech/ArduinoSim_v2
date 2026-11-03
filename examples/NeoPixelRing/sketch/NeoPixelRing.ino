// NeoPixelRing: Adafruit_NeoPixel runs a rainbow around a 16-LED WS2812 ring on D6.

#include <Adafruit_NeoPixel.h>

const int ledPin = 6;
const int ledCount = 16;

Adafruit_NeoPixel ring(ledCount, ledPin, NEO_GRB + NEO_KHZ800);

void setup() {
  ring.begin();
  ring.setBrightness(80);
  ring.show();
}

void loop() {
  static uint16_t hue = 0;
  for (int i = 0; i < ledCount; i++) {
    ring.setPixelColor(i, ring.gamma32(ring.ColorHSV(hue + i * 65536L / ledCount)));
  }
  ring.show();
  hue += 256;
  delay(20);
}