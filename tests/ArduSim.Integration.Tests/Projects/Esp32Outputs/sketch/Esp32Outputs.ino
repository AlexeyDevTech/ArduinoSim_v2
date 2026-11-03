// Esp32Outputs: ESP32Servo, Adafruit_NeoPixel, FastLED and the SD library on the ESP32 DevKit:
// a servo on GPIO 13, a NeoPixel strip on GPIO 4, a FastLED strip on GPIO 2, an SD card on VSPI.

#include <ESP32Servo.h>
#include <Adafruit_NeoPixel.h>
#include <FastLED.h>
#include <SD.h>

Servo servo;
Adafruit_NeoPixel strip(3, 4, NEO_GRB + NEO_KHZ800);
CRGB leds[2];

void setup() {
  Serial.begin(115200);
  ESP32PWM::allocateTimer(0);
  servo.setPeriodHertz(50);
  servo.attach(13, 500, 2400);
  servo.write(45);

  strip.begin();
  strip.setPixelColor(1, strip.Color(10, 20, 30));
  strip.show();

  FastLED.addLeds<WS2812B, 2, GRB>(leds, 2);
  leds[0] = CRGB::Green;
  FastLED.show();

  if (SD.begin(5)) {
    File f = SD.open("esp32.txt", FILE_WRITE);
    f.println("written by the ESP32");
    f.close();
    Serial.println(SD.exists("esp32.txt") ? "sd ok" : "sd missing");
  } else {
    Serial.println("sd failed");
  }
}

void loop() { delay(10); }