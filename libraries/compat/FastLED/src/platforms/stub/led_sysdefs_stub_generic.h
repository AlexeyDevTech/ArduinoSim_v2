// ArduSim (libraries/compat/FastLED/README.md): replaces FastLED 3.9.20's stub system definitions.
// The originals declare the Arduino functions themselves, with types that clash with the
// simulator's Arduino.h; here they come from Arduino.h.
#ifndef __INC_LED_SYSDEFS_STUB_H
#define __INC_LED_SYSDEFS_STUB_H

#ifndef FASTLED_STUB_IMPL
#define FASTLED_STUB_IMPL
#endif

#include <stdint.h>
#include <Arduino.h>

#ifndef FASTLED_HAS_MILLIS
#define FASTLED_HAS_MILLIS 1
#endif

#define FASTLED_USE_PROGMEM 0
#define FASTLED_ALLOW_INTERRUPTS 1
#define INTERRUPT_THRESHOLD 0

#ifndef digitalPinToBitMask
#define digitalPinToBitMask(P) ( 0 )
#endif
#ifndef digitalPinToPort
#define digitalPinToPort(P) ( 0 )
#endif
#ifndef portOutputRegister
#define portOutputRegister(P) ( 0 )
#endif
#ifndef portInputRegister
#define portInputRegister(P) ( 0 )
#endif

typedef volatile uint32_t RoReg;
typedef volatile uint32_t RwReg;

#endif // __INC_LED_SYSDEFS_STUB_H