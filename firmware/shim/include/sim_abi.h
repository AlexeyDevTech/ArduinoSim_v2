/*
 * sim_abi.h - the versioned C ABI between firmware.dll (sketch + Arduino shim)
 * and ArduSim.Runner.
 *
 * STATUS: draft for M0 review (docs/adr/ADR-001-simulation-approach.md).
 * Nothing outside firmware/shim and ArduSim.Runner may depend on this layout.
 *
 * Rules:
 *  - Plain C, fixed-width integers, no structs passed by value.
 *  - All functions run on the Runner's simulation thread. The host advances
 *    virtual time only inside these calls (docs/adr/ADR-005-virtual-time.md).
 *  - Minor version bump: functions appended to the end of SimHostApi.
 *    Major version bump: anything else.
 */
#ifndef SIM_ABI_H
#define SIM_ABI_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define SIM_ABI_VERSION_MAJOR 1
#define SIM_ABI_VERSION_MINOR 0
#define SIM_ABI_VERSION ((uint32_t)((SIM_ABI_VERSION_MAJOR << 16) | SIM_ABI_VERSION_MINOR))

#if defined(_WIN32)
#define SIM_FW_EXPORT __declspec(dllexport)
#else
#define SIM_FW_EXPORT __attribute__((visibility("default")))
#endif

/* Name of the process environment variable through which the Runner publishes
 * the SimHostApi address (hex) before it loads firmware.dll, so that C++ static
 * constructors in the sketch can reach the host before sim_fw_init runs. */
#define SIM_HOST_API_ENV "ARDUSIM_HOST_API"

typedef enum SimLogLevel {
    SIM_LOG_DEBUG = 0,
    SIM_LOG_INFO = 1,
    SIM_LOG_WARNING = 2,
    SIM_LOG_ERROR = 3
} SimLogLevel;

/* Values match the Arduino AVR core so the shim can pass them through. */
typedef enum SimPinMode {
    SIM_PIN_INPUT = 0,
    SIM_PIN_OUTPUT = 1,
    SIM_PIN_INPUT_PULLUP = 2
} SimPinMode;

typedef enum SimInterruptMode {
    SIM_INT_LOW = 0,
    SIM_INT_CHANGE = 1,
    SIM_INT_FALLING = 2,
    SIM_INT_RISING = 3
} SimInterruptMode;

/* Written by the firmware's vectored exception handler right before the
 * process dies; lives in memory shared with ArduSim.App so it survives. */
typedef struct SimCrashInfo {
    uint32_t magic;             /* 0x48535253 'SRSH' once written */
    uint32_t exception_code;    /* NTSTATUS, e.g. 0xC0000005 */
    uint64_t exception_address;
    uint64_t firmware_base;     /* load address of firmware.dll, for RVA */
    uint64_t access_address;    /* faulting data address for access violations */
} SimCrashInfo;

typedef struct SimHostApi {
    uint32_t struct_size; /* sizeof(SimHostApi) as compiled into the host */
    uint32_t abi_version; /* SIM_ABI_VERSION of the host */

    /* GPIO. Pins use Arduino board numbering (Nano: D0-D13 = 0-13, A0-A7 = 14-21). */
    void (*pin_mode)(int32_t pin, int32_t mode);
    void (*digital_write)(int32_t pin, int32_t level);
    int32_t (*digital_read)(int32_t pin);
    int32_t (*analog_read)(int32_t pin);               /* raw ADC counts at the board resolution */
    void (*analog_write)(int32_t pin, int32_t value);  /* PWM duty 0-255 on Nano */
    void (*analog_reference)(int32_t mode);
    void (*tone)(int32_t pin, uint32_t frequency_hz, uint32_t duration_ms); /* 0 = until no_tone */
    void (*no_tone)(int32_t pin);

    /* Virtual time. */
    uint64_t (*now_us)(void);
    void (*delay_us)(uint32_t us);
    void (*yield)(void);

    /* UART. Port 0 is Serial. */
    void (*serial_begin)(int32_t port, uint32_t baud, uint32_t config);
    void (*serial_end)(int32_t port);
    int32_t (*serial_write)(int32_t port, const uint8_t* data, int32_t length); /* blocks while TX is full */
    int32_t (*serial_read)(int32_t port);  /* -1 when empty */
    int32_t (*serial_peek)(int32_t port);  /* -1 when empty */
    int32_t (*serial_available)(int32_t port);
    int32_t (*serial_available_for_write)(int32_t port);
    void (*serial_flush)(int32_t port);    /* blocks until TX has drained */

    /* I2C master. */
    void (*i2c_begin)(void);
    void (*i2c_end)(void);
    void (*i2c_set_clock)(uint32_t hz);
    int32_t (*i2c_start)(uint8_t address7, int32_t read); /* 1 = ACK; repeated start if no stop since last start */
    int32_t (*i2c_write)(uint8_t value);                   /* 1 = ACK */
    uint8_t (*i2c_read)(int32_t ack);
    void (*i2c_stop)(void);

    /* SPI master (hardware peripheral; bit-banged SPI goes through GPIO). */
    void (*spi_begin)(void);
    void (*spi_end)(void);
    void (*spi_settings)(uint32_t clock_hz, int32_t bit_order, int32_t data_mode);
    uint8_t (*spi_transfer)(uint8_t value);

    /* EEPROM, persisted per project by the host. */
    uint8_t (*eeprom_read)(int32_t address);
    void (*eeprom_write)(int32_t address, uint8_t value);

    /* External interrupts. The firmware keeps the ISR pointers; the host calls
     * sim_fw_dispatch_interrupt(interrupt_number) at a scheduling point. */
    void (*attach_interrupt)(int32_t interrupt_number, int32_t mode);
    void (*detach_interrupt)(int32_t interrupt_number);
    void (*set_interrupts_enabled)(int32_t enabled);

    /* Diagnostics. Strings are UTF-8 and NUL-terminated. */
    void (*log)(int32_t level, const char* message);
    void (*fatal)(const char* message); /* never returns */
    SimCrashInfo* crash_info;
} SimHostApi;

/* Exports of firmware.dll. */
SIM_FW_EXPORT uint32_t sim_fw_abi_version(void);
SIM_FW_EXPORT int32_t sim_fw_init(const SimHostApi* host); /* 0 = ok, otherwise incompatible host */
SIM_FW_EXPORT void sim_fw_setup(void);
SIM_FW_EXPORT void sim_fw_loop(void);
SIM_FW_EXPORT void sim_fw_dispatch_interrupt(int32_t id);

#ifdef __cplusplus
}
#endif

#endif /* SIM_ABI_H */
