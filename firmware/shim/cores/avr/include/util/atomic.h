/* util/atomic.h - ATOMIC_BLOCK on top of the simulator's interrupt flag. */
#ifndef _UTIL_ATOMIC_H_
#define _UTIL_ATOMIC_H_

#include <avr/interrupt.h>
#include <stdint.h>

static inline void sim_atomic_restore(const uint8_t* enabled) { sim_interrupts_restore(*enabled); }
static inline void sim_atomic_force_on(const uint8_t* unused) { (void)unused; sim_interrupts_enable(); }
static inline void sim_atomic_force_off(const uint8_t* unused) { (void)unused; sim_interrupts_disable(); }

#define ATOMIC_RESTORESTATE uint8_t sim_sreg_save __attribute__((__cleanup__(sim_atomic_restore))) = sim_interrupts_save_disable()
#define ATOMIC_FORCEON uint8_t sim_sreg_save __attribute__((__cleanup__(sim_atomic_force_on))) = sim_interrupts_save_disable()
#define NONATOMIC_RESTORESTATE uint8_t sim_sreg_save __attribute__((__cleanup__(sim_atomic_restore))) = sim_interrupts_save_enable()
#define NONATOMIC_FORCEOFF uint8_t sim_sreg_save __attribute__((__cleanup__(sim_atomic_force_off))) = sim_interrupts_save_enable()

#define ATOMIC_BLOCK(type) for (type, sim_todo = 1; sim_todo; sim_todo = 0)
#define NONATOMIC_BLOCK(type) for (type, sim_todo = 1; sim_todo; sim_todo = 0)

#endif /* _UTIL_ATOMIC_H_ */
