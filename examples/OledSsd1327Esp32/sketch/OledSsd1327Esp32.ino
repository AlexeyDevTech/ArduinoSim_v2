// OledSsd1327Esp32: Adafruit_SSD1327 on a 1.5" 128x128 grayscale OLED over I2C (address 0x3D,
// SDA 21, SCL 22): sixteen gray levels in stripes and a title. The 8 KB frame buffer is why this
// one runs on the ESP32: it would not fit in a Nano's 2 KB of RAM.

#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1327.h>

Adafruit_SSD1327 display(128, 128, &Wire, -1, 1000000);

void setup() {
  Serial.begin(115200);
  if (!display.begin(0x3D)) {
    Serial.println("SSD1327 not found");
    while (true) delay(10);
  }
  display.clearDisplay();
  for (int level = 0; level < 16; level++) {
    display.fillRect(level * 8, 32, 8, 96, level);
  }
  display.setTextSize(1);
  display.setTextColor(SSD1327_WHITE);
  display.setCursor(16, 10);
  display.print("16 gray levels");
  display.display();
  Serial.println("Stripes drawn");
}

void loop() {
  delay(1000);
}