/*
 * sim/timers.h - periodic timer interrupts (ABI 2.2, docs/adr/ADR-011-sensors.md).
 *
 * The simulator has no timer registers; what the cores' hardware timers and libraries such as
 * IRremote (through libraries/compat) need is "call this function every N microseconds, as an
 * interrupt". The handler runs at a scheduling point like an attachInterrupt() ISR: never while
 * interrupts are off, never nested.
 */
#ifndef SIM_TIMERS_H
#define SIM_TIMERS_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* A free timer number, reserved until sim_timer_release(); -1 when all are in use. */
int sim_timer_claim(void);
void sim_timer_release(int timer);

/* Calls handler(arg) every period_ns nanoseconds of virtual time from now (at least 5 us). */
void sim_timer_start(int timer, uint64_t period_ns, void (*handler)(void*), void* arg);
void sim_timer_stop(int timer);

#ifdef __cplusplus
}

namespace sim {
/* For the cores' sim_fw_dispatch_interrupt: runs the timer handler behind an interrupt id and
 * returns true, or returns false for ids that are not timers. */
bool dispatch_timer(int32_t id);
}  // namespace sim
#endif

#endif /* SIM_TIMERS_H */
