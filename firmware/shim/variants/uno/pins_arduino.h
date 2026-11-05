/* pins_arduino.h - Arduino Uno R3 (ATmega328P) pin map: the Nano's, without A6 and A7. */
#ifndef Pins_Arduino_h
#define Pins_Arduino_h

#include <stdint.h>

#define NUM_DIGITAL_PINS 20
#define NUM_ANALOG_INPUTS 6
#define analogInputToDigitalPin(p) (((p) < 6) ? (p) + 14 : -1)
#define digitalPinHasPWM(p) ((p) == 3 || (p) == 5 || (p) == 6 || (p) == 9 || (p) == 10 || (p) == 11)
#define digitalPinToInterrupt(p) ((p) == 2 ? 0 : ((p) == 3 ? 1 : NOT_AN_INTERRUPT))

#define PIN_SPI_SS (10)
#define PIN_SPI_MOSI (11)
#define PIN_SPI_MISO (12)
#define PIN_SPI_SCK (13)
static const uint8_t SS = PIN_SPI_SS;
static const uint8_t MOSI = PIN_SPI_MOSI;
static const uint8_t MISO = PIN_SPI_MISO;
static const uint8_t SCK = PIN_SPI_SCK;

#define PIN_WIRE_SDA (18)
#define PIN_WIRE_SCL (19)
static const uint8_t SDA = PIN_WIRE_SDA;
static const uint8_t SCL = PIN_WIRE_SCL;

#define LED_BUILTIN 13

#define PIN_A0 (14)
#define PIN_A1 (15)
#define PIN_A2 (16)
#define PIN_A3 (17)
#define PIN_A4 (18)
#define PIN_A5 (19)
static const uint8_t A0 = PIN_A0;
static const uint8_t A1 = PIN_A1;
static const uint8_t A2 = PIN_A2;
static const uint8_t A3 = PIN_A3;
static const uint8_t A4 = PIN_A4;
static const uint8_t A5 = PIN_A5;

#endif /* Pins_Arduino_h */
