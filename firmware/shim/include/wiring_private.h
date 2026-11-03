/* wiring_private.h - the core's internal header, which libraries (Adafruit's TFT drivers) include
 * for the cbi/sbi register bit macros. Register writes go through the simulator's register
 * proxies on AVR; on the other cores the macros act on plain variables. */
#ifndef WiringPrivate_h
#define WiringPrivate_h

#include "Arduino.h"

#ifndef cbi
#define cbi(sfr, bit) ((sfr) &= ~(1 << (bit)))
#endif
#ifndef sbi
#define sbi(sfr, bit) ((sfr) |= (1 << (bit)))
#endif

#endif
