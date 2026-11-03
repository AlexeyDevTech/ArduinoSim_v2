/*
 * avr/eeprom.h - EEPROM access by explicit address, backed by the simulator
 * (persisted per project in eeprom.bin). EEMEM variables need linker-placed
 * addresses and are rejected at compile time.
 */
#ifndef _AVR_EEPROM_H_
#define _AVR_EEPROM_H_

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif
uint8_t sim_eeprom_read(int32_t address);
void sim_eeprom_write(int32_t address, uint8_t value);
void sim_eeprom_update(int32_t address, uint8_t value);
#ifdef __cplusplus
}
#endif

#define EEMEM _Pragma("GCC error \"EEMEM variables are not supported by the simulator; use EEPROM.get/put or eeprom_read_byte with explicit addresses\"")

#define eeprom_is_ready() 1
#define eeprom_busy_wait() \
    do {                   \
    } while (0)

#define SIM_EEPROM_ADDRESS(p) ((int32_t)(intptr_t)(p))

static inline uint8_t eeprom_read_byte(const uint8_t* p) { return sim_eeprom_read(SIM_EEPROM_ADDRESS(p)); }

static inline void eeprom_read_block(void* dst, const void* src, size_t n)
{
    for (size_t i = 0; i < n; i++) {
        ((uint8_t*)dst)[i] = sim_eeprom_read(SIM_EEPROM_ADDRESS(src) + (int32_t)i);
    }
}

static inline uint16_t eeprom_read_word(const uint16_t* p)
{
    uint16_t v;
    eeprom_read_block(&v, p, sizeof v);
    return v;
}

static inline uint32_t eeprom_read_dword(const uint32_t* p)
{
    uint32_t v;
    eeprom_read_block(&v, p, sizeof v);
    return v;
}

static inline float eeprom_read_float(const float* p)
{
    float v;
    eeprom_read_block(&v, p, sizeof v);
    return v;
}

static inline void eeprom_write_byte(uint8_t* p, uint8_t value) { sim_eeprom_write(SIM_EEPROM_ADDRESS(p), value); }

static inline void eeprom_write_block(const void* src, void* dst, size_t n)
{
    for (size_t i = 0; i < n; i++) {
        sim_eeprom_write(SIM_EEPROM_ADDRESS(dst) + (int32_t)i, ((const uint8_t*)src)[i]);
    }
}

static inline void eeprom_update_byte(uint8_t* p, uint8_t value) { sim_eeprom_update(SIM_EEPROM_ADDRESS(p), value); }

static inline void eeprom_update_block(const void* src, void* dst, size_t n)
{
    for (size_t i = 0; i < n; i++) {
        sim_eeprom_update(SIM_EEPROM_ADDRESS(dst) + (int32_t)i, ((const uint8_t*)src)[i]);
    }
}

static inline void eeprom_write_word(uint16_t* p, uint16_t value) { eeprom_write_block(&value, p, sizeof value); }
static inline void eeprom_write_dword(uint32_t* p, uint32_t value) { eeprom_write_block(&value, p, sizeof value); }
static inline void eeprom_write_float(float* p, float value) { eeprom_write_block(&value, p, sizeof value); }
static inline void eeprom_update_word(uint16_t* p, uint16_t value) { eeprom_update_block(&value, p, sizeof value); }
static inline void eeprom_update_dword(uint32_t* p, uint32_t value) { eeprom_update_block(&value, p, sizeof value); }
static inline void eeprom_update_float(float* p, float value) { eeprom_update_block(&value, p, sizeof value); }

#endif /* _AVR_EEPROM_H_ */
