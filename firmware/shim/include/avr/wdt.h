/*
 * avr/wdt.h - the watchdog is not simulated: wdt_enable() logs a warning once
 * and the board never resets; wdt_reset() and wdt_disable() do nothing.
 */
#ifndef _AVR_WDT_H_
#define _AVR_WDT_H_

#define WDTO_15MS 0
#define WDTO_30MS 1
#define WDTO_60MS 2
#define WDTO_120MS 3
#define WDTO_250MS 4
#define WDTO_500MS 5
#define WDTO_1S 6
#define WDTO_2S 7
#define WDTO_4S 8
#define WDTO_8S 9

#ifdef __cplusplus
extern "C" {
#endif
void sim_wdt_enable(int timeout);
#ifdef __cplusplus
}
#endif

#define wdt_enable(timeout) sim_wdt_enable(timeout)
#define wdt_reset() ((void)0)
#define wdt_disable() ((void)0)

#endif /* _AVR_WDT_H_ */
