/*
 * core_defs.h - the Arduino AVR core's constants and types, included by Arduino.h
 * before the shared API.
 */
#ifndef Core_defs_h
#define Core_defs_h

#include <stdint.h>

#include <avr/interrupt.h>
#include <avr/io.h>

#ifndef F_CPU
#define F_CPU 16000000L
#endif

#define INPUT 0x0
#define OUTPUT 0x1
#define INPUT_PULLUP 0x2

#define CHANGE 1
#define FALLING 2
#define RISING 3

#define DEFAULT 1
#define EXTERNAL 0
#define INTERNAL 3

#define NOT_A_PIN 0
#define NOT_A_PORT 0
#define NOT_AN_INTERRUPT -1

#define interrupts() sei()
#define noInterrupts() cli()

typedef uint16_t word; /* 16 bits as on AVR, although int is 32 bits here */

#endif /* Core_defs_h */