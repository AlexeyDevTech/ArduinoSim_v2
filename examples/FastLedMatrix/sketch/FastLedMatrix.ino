// FastLedMatrix: FastLED draws a moving rainbow on an 8x8 WS2812 panel on D5,
// wired serpentine (every other row runs backwards), as most panels are.

#include <FastLED.h>

#define DATA_PIN 5
#define WIDTH 8
#define HEIGHT 8

CRGB leds[WIDTH * HEIGHT];

uint16_t xy(uint8_t x, uint8_t y) {
  return (y % 2 == 0) ? y * WIDTH + x : y * WIDTH + (WIDTH - 1 - x);
}

void setup() {
  FastLED.addLeds<WS2812B, DATA_PIN, GRB>(leds, WIDTH * HEIGHT);
  FastLED.setBrightness(64);
}

void loop() {
  uint8_t t = millis() / 10;
  for (uint8_t y = 0; y < HEIGHT; y++) {
    for (uint8_t x = 0; x < WIDTH; x++) {
      leds[xy(x, y)] = CHSV(t + (x + y) * 16, 255, 255);
    }
  }
  FastLED.show();
  delay(20);
}