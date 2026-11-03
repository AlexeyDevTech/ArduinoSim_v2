// Nokia5110: Adafruit_PCD8544 on the Nokia 5110 LCD (hardware SPI: DC D5, CE D4, RST D3; DIN D11,
// CLK D13). It draws a sine wave and a counter; the backlight on D6 is dimmed with PWM.
// The contrast (V_OP) matters on these glasses: try other setContrast() values.

#include <SPI.h>
#include <Adafruit_GFX.h>
#include <Adafruit_PCD8544.h>

Adafruit_PCD8544 display(5, 4, 3);

void setup() {
  Serial.begin(9600);
  display.begin();
  display.setContrast(60);
  analogWrite(6, 160);
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(BLACK);
  display.setCursor(0, 0);
  display.println("Nokia 5110");
  for (int x = 0; x < 84; x++) {
    display.drawPixel(x, 30 + 10 * sin(x * 2 * PI / 42), BLACK);
  }
  display.display();
  Serial.println("Ready");
}

void loop() {
  static int count = 0;
  display.fillRect(0, 10, 84, 8, WHITE);
  display.setCursor(0, 10);
  display.print("Count: ");
  display.print(count++);
  display.display();
  delay(500);
}