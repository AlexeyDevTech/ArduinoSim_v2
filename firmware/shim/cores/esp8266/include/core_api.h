/* core_api.h - API of the ESP8266 Arduino core beyond the shared Arduino.h, included at its end. */
#ifndef Core_api_h
#define Core_api_h

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void analogWriteRange(uint32_t range);
void analogWriteFreq(uint32_t freq);
void analogWriteResolution(int res);
uint64_t micros64(void);
void optimistic_yield(uint32_t interval_us);
void esp_yield(void);
void esp_delay(unsigned long ms);
uint32_t system_get_time(void);
uint32_t ESP_getChipId(void);

#ifdef __cplusplus
}
#endif

#ifdef __cplusplus
#include "Esp.h"

void attachInterrupt(uint8_t pin, void (*userFunc)(void), int mode);
void attachInterruptArg(uint8_t pin, void (*handler)(void*), void* arg, int mode);
long secureRandom(long howbig);
long secureRandom(long howsmall, long howbig);
#endif

#endif /* Core_api_h */