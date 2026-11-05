/*
 * avr/io.h - ATmega328P register names (ATmega2560 ports on the Mega).
 *
 * DDRx/PORTx/PINx are proxies routed to the pin API (D0-D7 = PORTD,
 * D8-D13 = PORTB, A0-A5 = PORTC; the Mega's ports A-L in registers.cpp). SREG carries the interrupt flag. Every other
 * register exists so code compiles, logs "register X is not simulated" on its
 * first write and reads as 0. Registers are C++ objects, so C sources cannot use them.
 */
#ifndef _AVR_IO_H_
#define _AVR_IO_H_

#include <stdint.h>

#define _BV(bit) (1 << (bit))
#define bit_is_set(sfr, bit) ((sfr) & _BV(bit))
#define bit_is_clear(sfr, bit) (!((sfr) & _BV(bit)))
#define loop_until_bit_is_set(sfr, bit) \
    do {                                \
    } while (bit_is_clear(sfr, bit))
#define loop_until_bit_is_clear(sfr, bit) \
    do {                                  \
    } while (bit_is_set(sfr, bit))

#if defined(SIM_BOARD_MEGA)
#define RAMSTART 0x200
#define RAMEND 0x21FF
#define XRAMEND RAMEND
#define E2END 0xFFF
#define E2PAGESIZE 8
#define FLASHEND 0x3FFFF
#define SPM_PAGESIZE 256
#else
#define RAMSTART 0x100
#define RAMEND 0x8FF
#define XRAMEND RAMEND
#define E2END 0x3FF
#define E2PAGESIZE 4
#define FLASHEND 0x7FFF
#define SPM_PAGESIZE 128
#endif

#ifdef __cplusplus
#include "sim/registers.h"
#endif

#include "sim/register_bits.h"

#endif /* _AVR_IO_H_ */
