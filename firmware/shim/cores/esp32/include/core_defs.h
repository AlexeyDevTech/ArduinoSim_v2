/*
 * core_defs.h - the Arduino-ESP32 core's constants and types (core 3.x values), included by
 * Arduino.h before the shared API (docs/adr/ADR-009-esp-boards.md).
 */
#ifndef Core_defs_h
#define Core_defs_h

#include <stdint.h>

#include "esp_arduino_version.h"

#ifndef F_CPU
#define F_CPU 240000000L
#endif

#define INPUT 0x01
#define OUTPUT 0x03
#define PULLUP 0x04
#define INPUT_PULLUP 0x05
#define PULLDOWN 0x08
#define INPUT_PULLDOWN 0x09
#define OPEN_DRAIN 0x10
#define OUTPUT_OPEN_DRAIN 0x13
#define ANALOG 0xC0

#define RISING 0x01
#define FALLING 0x02
#define CHANGE 0x03
#define ONLOW 0x04
#define ONHIGH 0x05
#define ONLOW_WE 0x0C
#define ONHIGH_WE 0x0D

#define NOT_A_PIN -1
#define NOT_A_PORT -1
#define NOT_AN_INTERRUPT -1
#define NOT_ON_TIMER 0

#ifdef __cplusplus
extern "C" {
#endif
void sim_interrupts_enable(void);
void sim_interrupts_disable(void);
#ifdef __cplusplus
}
#endif

#define interrupts() sim_interrupts_enable()
#define noInterrupts() sim_interrupts_disable()
#define sei() sim_interrupts_enable()
#define cli() sim_interrupts_disable()

typedef unsigned int word;

/* Placement attributes of ESP-IDF: everything is ordinary memory here. RTC_DATA_ATTR variables
 * do not survive deep sleep or a restart (the firmware is loaded afresh), unlike on the chip. */
#define IRAM_ATTR
#define DRAM_ATTR
#define ICACHE_RAM_ATTR
#define RTC_DATA_ATTR
#define RTC_NOINIT_ATTR
#define RTC_RODATA_ATTR
#define WORD_ALIGNED_ATTR __attribute__((aligned(4)))

#endif /* Core_defs_h */
