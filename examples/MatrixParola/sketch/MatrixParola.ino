// MatrixParola: MD_Parola scrolls text across four MAX7219 8x8 modules (an FC-16 board)
// on hardware SPI: DIN on D11, CLK on D13, CS on D10.

#include <MD_Parola.h>
#include <MD_MAX72xx.h>
#include <SPI.h>

#define HARDWARE_TYPE MD_MAX72XX::FC16_HW
#define MAX_DEVICES 4
#define CS_PIN 10

MD_Parola display = MD_Parola(HARDWARE_TYPE, CS_PIN, MAX_DEVICES);

void setup() {
  display.begin();
  display.setIntensity(4);
  display.displayText("Hello from ArduSim!", PA_LEFT, 40, 0, PA_SCROLL_LEFT, PA_SCROLL_LEFT);
}

void loop() {
  if (display.displayAnimate()) {
    display.displayReset();
  }
}