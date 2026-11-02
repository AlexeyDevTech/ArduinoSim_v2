/*
 * avr/interrupt.h - cli()/sei() reach the simulator, which never dispatches an
 * ISR while interrupts are disabled. ISR() bodies compile but only external
 * interrupts (attachInterrupt) ever run: timer and other vectors are not simulated.
 */
#ifndef _AVR_INTERRUPT_H_
#define _AVR_INTERRUPT_H_

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif
void sim_interrupts_enable(void);
void sim_interrupts_disable(void);
uint8_t sim_interrupts_save_disable(void);
uint8_t sim_interrupts_save_enable(void);
void sim_interrupts_restore(uint8_t enabled);
#ifdef __cplusplus
}
#endif

#define sei() sim_interrupts_enable()
#define cli() sim_interrupts_disable()
#define reti()

#define ISR_BLOCK
#define ISR_NOBLOCK
#define ISR_NAKED
#define ISR_ALIASOF(v)
#define BADISR_vect sim_bad_isr

#ifdef __cplusplus
#define ISR(vector, ...)            \
    extern "C" void vector(void);   \
    extern "C" void vector(void)
#else
#define ISR(vector, ...) void vector(void)
#endif
#define SIGNAL(vector) ISR(vector)
#define EMPTY_INTERRUPT(vector) \
    ISR(vector) {}
#define ISR_ALIAS(vector, target_vector)

#endif /* _AVR_INTERRUPT_H_ */
