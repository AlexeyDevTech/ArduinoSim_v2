/* util/delay.h - busy-wait delays become virtual-time delays. */
#ifndef _UTIL_DELAY_H_
#define _UTIL_DELAY_H_

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif
void sim_delay_us(uint32_t us);
#ifdef __cplusplus
}
#endif

static inline void _delay_us(double us) { sim_delay_us((uint32_t)us); }
static inline void _delay_ms(double ms) { sim_delay_us((uint32_t)(ms * 1000.0)); }

#endif /* _UTIL_DELAY_H_ */
