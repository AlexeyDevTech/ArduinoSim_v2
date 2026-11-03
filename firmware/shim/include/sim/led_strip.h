/*
 * sim/led_strip.h - one frame for WS2812-style addressable LEDs (ABI 2.3, docs/adr/ADR-012-outputs.md).
 *
 * The real protocol is an 800 kHz bit stream timed in fractions of a microsecond, which the pin
 * calls cannot make; the LED libraries in libraries/compat hand the whole frame over instead. The
 * host charges its wire time and gives it to the strips on the pin's net.
 */
#ifndef SIM_LED_STRIP_H
#define SIM_LED_STRIP_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* data: the bytes in wire order (a WS2812B takes G, R, B per LED); khz: 800 or 400. */
void sim_led_strip_write(uint8_t pin, const uint8_t* data, uint32_t length, int khz);

#ifdef __cplusplus
}
#endif

#endif /* SIM_LED_STRIP_H */