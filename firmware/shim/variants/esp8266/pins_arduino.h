/* pins_arduino.h - ESP8266 boards (NodeMCU v3, Wemos D1 mini): Arduino pin numbers are GPIO numbers, A0 is 17. */
#ifndef Pins_Arduino_h
#define Pins_Arduino_h

#include <stdint.h>

#define EXTERNAL_NUM_INTERRUPTS 16
#define NUM_DIGITAL_PINS 17
#define NUM_ANALOG_INPUTS 1
#define analogInputToDigitalPin(p) (((p) == 0) ? 17 : -1)
#define digitalPinToInterrupt(p) (((p) < EXTERNAL_NUM_INTERRUPTS) ? (p) : NOT_AN_INTERRUPT)
#define digitalPinHasPWM(p) (((p) < NUM_DIGITAL_PINS) ? 1 : 0)
#define isFlashInterfacePin(p) ((p) >= 6 && (p) <= 11)

static const uint8_t D0 = 16;
static const uint8_t D1 = 5;
static const uint8_t D2 = 4;
static const uint8_t D3 = 0;
static const uint8_t D4 = 2;
static const uint8_t D5 = 14;
static const uint8_t D6 = 12;
static const uint8_t D7 = 13;
static const uint8_t D8 = 15;
static const uint8_t RX = 3;
static const uint8_t TX = 1;
static const uint8_t A0 = 17;

#define PIN_A0 (17)
#define LED_BUILTIN 2
#define BUILTIN_LED 2

#define PIN_WIRE_SDA (4)
#define PIN_WIRE_SCL (5)
static const uint8_t SDA = PIN_WIRE_SDA;
static const uint8_t SCL = PIN_WIRE_SCL;

#define PIN_SPI_SS (15)
#define PIN_SPI_MOSI (13)
#define PIN_SPI_MISO (12)
#define PIN_SPI_SCK (14)
static const uint8_t SS = PIN_SPI_SS;
static const uint8_t MOSI = PIN_SPI_MOSI;
static const uint8_t MISO = PIN_SPI_MISO;
static const uint8_t SCK = PIN_SPI_SCK;

#endif /* Pins_Arduino_h */