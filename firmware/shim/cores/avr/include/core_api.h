/* core_api.h - API of the Arduino AVR core beyond the shared Arduino.h, included at its end. */
#ifndef Core_api_h
#define Core_api_h

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void analogReference(uint8_t mode);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* Core_api_h */