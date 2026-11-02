/*
 * Arduino.h - the Arduino core API for ArduSim.
 *
 * Behaviour follows the Arduino AVR core; every hardware access goes to the
 * simulator through sim_abi.h. Differences from AVR that a sketch can notice
 * are listed in README.md ("About simulation accuracy").
 */
#ifndef Arduino_h
#define Arduino_h

#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include <avr/interrupt.h>
#include <avr/io.h>
#include <avr/pgmspace.h>

#include "binary.h"

#ifndef F_CPU
#define F_CPU 16000000L
#endif

#define HIGH 0x1
#define LOW 0x0

#define INPUT 0x0
#define OUTPUT 0x1
#define INPUT_PULLUP 0x2

#define PI 3.1415926535897932384626433832795
#define HALF_PI 1.5707963267948966192313216916398
#define TWO_PI 6.283185307179586476925286766559
#define DEG_TO_RAD 0.017453292519943295769236907684886
#define RAD_TO_DEG 57.295779513082320876798154814105
#define EULER 2.718281828459045235360287471352

#define SERIAL 0x0
#define DISPLAY 0x1

#define LSBFIRST 0
#define MSBFIRST 1

#define CHANGE 1
#define FALLING 2
#define RISING 3

#define DEFAULT 1
#define EXTERNAL 0
#define INTERNAL 3

#define NOT_A_PIN 0
#define NOT_A_PORT 0
#define NOT_AN_INTERRUPT -1

/* min/max are templates in C++ (as in ArduinoCore-API) so the C++ standard library stays usable. */
#ifndef __cplusplus
#define min(a, b) ((a) < (b) ? (a) : (b))
#define max(a, b) ((a) > (b) ? (a) : (b))
#endif
#define constrain(amt, low, high) ((amt) < (low) ? (low) : ((amt) > (high) ? (high) : (amt)))
#define radians(deg) ((deg) * DEG_TO_RAD)
#define degrees(rad) ((rad) * RAD_TO_DEG)
#define sq(x) ((x) * (x))

#define interrupts() sei()
#define noInterrupts() cli()

#define clockCyclesPerMicrosecond() (F_CPU / 1000000L)
#define clockCyclesToMicroseconds(a) ((a) / clockCyclesPerMicrosecond())
#define microsecondsToClockCycles(a) ((a) * clockCyclesPerMicrosecond())

#define lowByte(w) ((uint8_t)((w) & 0xff))
#define highByte(w) ((uint8_t)((w) >> 8))

#define bitRead(value, bit) (((value) >> (bit)) & 0x01)
#define bitSet(value, bit) ((value) |= (1UL << (bit)))
#define bitClear(value, bit) ((value) &= ~(1UL << (bit)))
#define bitToggle(value, bit) ((value) ^= (1UL << (bit)))
#define bitWrite(value, bit, bitvalue) ((bitvalue) ? bitSet(value, bit) : bitClear(value, bit))
#define bit(b) (1UL << (b))

typedef bool boolean;
typedef uint8_t byte;
typedef uint16_t word; /* 16 bits as on AVR, although int is 32 bits here */

#ifdef __cplusplus
extern "C" {
#endif

void init(void);
void initVariant(void);
void yield(void);

void pinMode(uint8_t pin, uint8_t mode);
void digitalWrite(uint8_t pin, uint8_t val);
int digitalRead(uint8_t pin);
int analogRead(uint8_t pin);
void analogReference(uint8_t mode);
void analogWrite(uint8_t pin, int val);

unsigned long millis(void);
unsigned long micros(void);
void delay(unsigned long ms);
void delayMicroseconds(unsigned int us);
unsigned long pulseIn(uint8_t pin, uint8_t state, unsigned long timeout);
unsigned long pulseInLong(uint8_t pin, uint8_t state, unsigned long timeout);

void shiftOut(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder, uint8_t val);
uint8_t shiftIn(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder);

void attachInterrupt(uint8_t interruptNum, void (*userFunc)(void), int mode);
void detachInterrupt(uint8_t interruptNum);

void setup(void);
void loop(void);

/* avr-libc <stdlib.h> functions that mingw-w64 does not provide (itoa, ltoa, ultoa it does). */
char* utoa(unsigned int value, char* buffer, int radix);
char* dtostrf(double val, signed char width, unsigned char prec, char* sout);
long random(void);
void srandom(unsigned long seed);
#define RANDOM_MAX 0x7FFFFFFF

#ifdef __cplusplus
} /* extern "C" */
#endif

#ifdef __cplusplus
#include "HardwareSerial.h"
#include "WCharacter.h"
#include "WString.h"

uint16_t makeWord(uint16_t w);
uint16_t makeWord(uint8_t h, uint8_t l);
#define word(...) makeWord(__VA_ARGS__)

unsigned long pulseIn(uint8_t pin, uint8_t state, unsigned long timeout = 1000000L);
unsigned long pulseInLong(uint8_t pin, uint8_t state, unsigned long timeout = 1000000L);

void tone(uint8_t pin, unsigned int frequency, unsigned long duration = 0);
void noTone(uint8_t pin);

long random(long howbig);
long random(long howsmall, long howbig);
void randomSeed(unsigned long seed);
long map(long value, long fromLow, long fromHigh, long toLow, long toHigh);

template <class T, class L>
auto min(const T& a, const L& b) -> decltype((b < a) ? b : a)
{
    return (b < a) ? b : a;
}

template <class T, class L>
auto max(const T& a, const L& b) -> decltype((b < a) ? b : a)
{
    return (a < b) ? b : a;
}
#endif

#include "pins_arduino.h"

#endif /* Arduino_h */
