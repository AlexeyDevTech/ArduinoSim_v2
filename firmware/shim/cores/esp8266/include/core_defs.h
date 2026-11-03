/*
 * core_defs.h - the ESP8266 Arduino core's constants and types (core 3.x values), included by
 * Arduino.h before the shared API (docs/adr/ADR-009-esp-boards.md).
 */
#ifndef Core_defs_h
#define Core_defs_h

#include <stdint.h>

#ifndef F_CPU
#define F_CPU 80000000L
#endif

#define INPUT 0x00
#define INPUT_PULLUP 0x02
#define INPUT_PULLDOWN_16 0x04
#define OUTPUT 0x01
#define OUTPUT_OPEN_DRAIN 0x03
#define WAKEUP_PULLUP 0x05
#define WAKEUP_PULLDOWN 0x07
#define SPECIAL 0xF8
#define FUNCTION_0 0x08
#define FUNCTION_1 0x18
#define FUNCTION_2 0x28
#define FUNCTION_3 0x38
#define FUNCTION_4 0x48

#define RISING 0x01
#define FALLING 0x02
#define CHANGE 0x03
#define ONLOW 0x04
#define ONHIGH 0x05
#define ONLOW_WE 0x0C
#define ONHIGH_WE 0x0D

#define DEFAULT 1
#define EXTERNAL 0

#define NOT_A_PIN -1
#define NOT_A_PORT -1
#define NOT_AN_INTERRUPT -1
#define NOT_ON_TIMER 0

#ifdef __cplusplus
extern "C" {
#endif
void sim_interrupts_enable(void);
void sim_interrupts_disable(void);
uint32_t sim_random32(void);
#ifdef __cplusplus
}
#endif

#define interrupts() sim_interrupts_enable()
#define noInterrupts() sim_interrupts_disable()
#define sei() sim_interrupts_enable()
#define cli() sim_interrupts_disable()

/* The hardware random number register; here a seeded generator, so runs repeat. */
#define RANDOM_REG32 sim_random32()

typedef unsigned int word;

/* Placement attributes: everything is ordinary memory here. */
#define IRAM_ATTR
#define ICACHE_RAM_ATTR
#define ICACHE_FLASH_ATTR
#define DRAM_ATTR

/* ADC_MODE(ADC_VCC) lets ESP.getVcc() measure the supply; the simulated supply is 3.3 V either way. */
#define ADC_TOUT 33
#define ADC_TOUT_MODE ADC_TOUT
#define ADC_VCC 255
#define ADC_VDD ADC_VCC
#define ADC_MODE(mode) static_assert((mode) == ADC_TOUT || (mode) == ADC_VCC, "ADC_MODE: ADC_TOUT or ADC_VCC")

#endif /* Core_defs_h */