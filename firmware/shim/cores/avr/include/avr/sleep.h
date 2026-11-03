/*
 * avr/sleep.h - sleep modes are not simulated: sleep_cpu() logs a warning once
 * and returns immediately, so the sketch keeps running instead of waiting for an interrupt.
 */
#ifndef _AVR_SLEEP_H_
#define _AVR_SLEEP_H_

#define SLEEP_MODE_IDLE 0
#define SLEEP_MODE_ADC 1
#define SLEEP_MODE_PWR_DOWN 2
#define SLEEP_MODE_PWR_SAVE 3
#define SLEEP_MODE_STANDBY 6
#define SLEEP_MODE_EXT_STANDBY 7

#ifdef __cplusplus
extern "C" {
#endif
void sim_sleep_cpu(void);
#ifdef __cplusplus
}
#endif

#define set_sleep_mode(mode) ((void)(mode))
#define sleep_enable() ((void)0)
#define sleep_disable() ((void)0)
#define sleep_bod_disable() ((void)0)
#define sleep_cpu() sim_sleep_cpu()
#define sleep_mode() sim_sleep_cpu()

#endif /* _AVR_SLEEP_H_ */
