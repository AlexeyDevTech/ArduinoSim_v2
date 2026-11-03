/*
 * sim_abi.h - the versioned C ABI between firmware.dll (sketch + Arduino shim)
 * and ArduSim.Runner.
 *
 * Version 2 (docs/adr/ADR-001-simulation-approach.md, ADR-009-esp-boards.md, ADR-011-sensors.md):
 * buses are numbered and may use chosen pins, PWM has a frequency and resolution.
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

#define SIM_ABI_VERSION_MAJOR 2
#define SIM_ABI_VERSION_MINOR 2
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

/* The first three match the Arduino AVR core so the AVR shim passes them through;
 * other cores map their constants to these. */
typedef enum SimPinMode {
    SIM_PIN_INPUT = 0,
    SIM_PIN_OUTPUT = 1,
    SIM_PIN_INPUT_PULLUP = 2,
    SIM_PIN_INPUT_PULLDOWN = 3,
    SIM_PIN_OUTPUT_OPEN_DRAIN = 4
} SimPinMode;

/* A pin argument of SIM_PIN_DEFAULT means the board's default pin for that bus signal. */
#define SIM_PIN_DEFAULT (-1)

typedef enum SimInterruptMode {
    SIM_INT_LOW = 0,
    SIM_INT_CHANGE = 1,
    SIM_INT_FALLING = 2,
    SIM_INT_RISING = 3
} SimInterruptMode;

/* Written by the firmware's vectored exception handler right before the
 * process dies; lives in memory shared with ArduSim.App so it survives.
 * The host provides at least 64 bytes here; fields are only appended. */
typedef struct SimCrashInfo {
    uint32_t magic;             /* 0x48535253 'SRSH' once written */
    uint32_t exception_status;  /* NTSTATUS, e.g. 0xC0000005 (not "exception_code": a macro in <excpt.h>) */
    uint64_t exception_address;
    uint64_t firmware_base;     /* load address of firmware.dll, for RVA */
    uint64_t access_address;    /* faulting data address for access violations */
    uint64_t return_address;    /* first return address into firmware.dll on the faulting stack, or 0 */
    uint32_t access_kind;       /* access violations: 0 read, 1 write, 8 execute */
    uint32_t reserved;
} SimCrashInfo;

typedef struct SimHostApi {
    uint32_t struct_size; /* sizeof(SimHostApi) as compiled into the host */
    uint32_t abi_version; /* SIM_ABI_VERSION of the host */

    /* GPIO. Pins use Arduino board numbering (Nano: D0-D13 = 0-13, A0-A7 = 14-21; ESP: GPIO numbers). */
    void (*pin_mode)(int32_t pin, int32_t mode);
    void (*digital_write)(int32_t pin, int32_t level);
    int32_t (*digital_read)(int32_t pin);
    int32_t (*analog_read)(int32_t pin);               /* raw ADC counts at the board's native resolution */
    void (*analog_write)(int32_t pin, int32_t duty);   /* PWM duty at the pin's resolution (Nano: 8 bits) */
    void (*pwm_config)(int32_t pin, uint32_t frequency_hz, int32_t resolution_bits); /* boards with configurable PWM */
    void (*dac_write)(int32_t pin, int32_t value);     /* 8-bit DAC output, boards that have one */
    void (*analog_reference)(int32_t mode);
    void (*tone)(int32_t pin, uint32_t frequency_hz, uint32_t duration_ms); /* 0 = until no_tone */
    void (*no_tone)(int32_t pin);

    /* Virtual time. */
    uint64_t (*now_us)(void);
    void (*delay_us)(uint32_t us);
    void (*yield)(void);

    /* UART. Port 0 is Serial, 1 Serial1, 2 Serial2. */
    void (*serial_begin)(int32_t port, uint32_t baud, uint32_t config, int32_t rx_pin, int32_t tx_pin);
    void (*serial_end)(int32_t port);
    int32_t (*serial_write)(int32_t port, const uint8_t* data, int32_t length); /* blocks while TX is full */
    int32_t (*serial_read)(int32_t port);  /* -1 when empty */
    int32_t (*serial_peek)(int32_t port);  /* -1 when empty */
    int32_t (*serial_available)(int32_t port);
    int32_t (*serial_available_for_write)(int32_t port);
    void (*serial_flush)(int32_t port);    /* blocks until TX has drained */

    /* I2C master. Bus 0 is Wire, 1 Wire1. */
    void (*i2c_begin)(int32_t bus, int32_t sda_pin, int32_t scl_pin);
    void (*i2c_end)(int32_t bus);
    void (*i2c_set_clock)(int32_t bus, uint32_t hz);
    int32_t (*i2c_start)(int32_t bus, uint8_t address7, int32_t read); /* 1 = ACK; repeated start if no stop since last start */
    int32_t (*i2c_write)(int32_t bus, uint8_t value);                   /* 1 = ACK */
    uint8_t (*i2c_read)(int32_t bus, int32_t ack);
    void (*i2c_stop)(int32_t bus);

    /* SPI master (hardware peripheral; bit-banged SPI goes through GPIO). Bus 0 is SPI (ESP32: VSPI), 1 HSPI. */
    void (*spi_begin)(int32_t bus, int32_t sck_pin, int32_t miso_pin, int32_t mosi_pin, int32_t ss_pin);
    void (*spi_end)(int32_t bus);
    void (*spi_settings)(int32_t bus, uint32_t clock_hz, int32_t bit_order, int32_t data_mode);
    uint8_t (*spi_transfer)(int32_t bus, uint8_t value);

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

    /* Key-value store (ESP Preferences), persisted per project by the host (ADR-009).
     * Names are UTF-8, at most 15 bytes; type is a SimKvType. */
    int32_t (*kv_get)(const char* name_space, const char* key, int32_t* type, uint8_t* buffer, int32_t capacity); /* stored length, -1 when absent */
    int32_t (*kv_put)(const char* name_space, const char* key, int32_t type, const uint8_t* data, int32_t length); /* 0 = stored */
    int32_t (*kv_remove)(const char* name_space, const char* key); /* 1 = removed */
    void (*kv_clear)(const char* name_space);

    /* Reboots the board: the host reloads the firmware and runs setup() again; never returns. */
    void (*restart)(void);

    /* Wi-Fi: the radio is simulated (ADR-010). Status values are the cores' wl_status_t.
     * IPv4 addresses are uint32 with the first octet in the lowest byte, as IPAddress keeps them. */
    void (*wifi_scan_start)(void);
    int32_t (*wifi_scan_count)(void); /* -1 while scanning, -2 when no scan was started */
    int32_t (*wifi_scan_entry)(int32_t index, char* ssid, int32_t ssid_capacity, int32_t* rssi, int32_t* channel, int32_t* open); /* 0, or -1 */
    void (*wifi_begin)(const char* ssid, const char* password);
    int32_t (*wifi_status)(void);
    void (*wifi_disconnect)(void);
    int32_t (*wifi_soft_ap)(const char* ssid, const char* password); /* 1 = the access point is up */
    uint32_t (*wifi_address)(int32_t which); /* 0 local IP, 1 gateway, 2 subnet, 3 DNS, 4 soft-AP IP */
    int32_t (*wifi_rssi)(void);

    /* Network: real sockets in the host once Wi-Fi is up (ADR-010). Handles are > 0; no call blocks. */
    int32_t (*net_connect)(const char* host, uint16_t port, int32_t tls, const char* ca_pem); /* tls: 0 none, 1 verify, 2 insecure */
    int32_t (*net_state)(int32_t handle);    /* 0 connecting, 1 connected, 2 closed by the peer (data may remain), -1 failed */
    int32_t (*net_available)(int32_t handle);
    int32_t (*net_read)(int32_t handle, uint8_t* buffer, int32_t capacity);    /* bytes read, 0 when none */
    int32_t (*net_write)(int32_t handle, const uint8_t* data, int32_t length); /* bytes accepted */
    void (*net_close)(int32_t handle);
    int32_t (*net_listen)(uint16_t port);    /* listening handle, or -1 */
    int32_t (*net_accept)(int32_t listener); /* connection handle, or 0 when none is waiting */
    int32_t (*net_remote)(int32_t handle, uint32_t* ip, uint16_t* port); /* 1 when known */
    int32_t (*net_resolve)(const char* host); /* lookup handle */
    int32_t (*net_resolve_result)(int32_t handle, uint32_t* ip); /* 0 pending, 1 done, -1 failed; the handle is freed once not pending */
    int32_t (*udp_open)(uint16_t port);       /* handle, or -1 */
    int32_t (*udp_send)(int32_t handle, const char* host, uint16_t port, const uint8_t* data, int32_t length); /* 1 = sent */
    int32_t (*udp_receive)(int32_t handle, uint8_t* buffer, int32_t capacity, uint32_t* ip, uint16_t* port); /* datagram length, -1 when none */

    /* 2.1: why the board last booted (SimResetReason), and deep sleep: nothing runs for the
     * given time, then the board boots with SIM_RESET_DEEP_SLEEP; never returns. */
    int32_t (*reset_reason)(void);
    void (*deep_sleep)(uint64_t microseconds);

    /* 2.2: periodic timer interrupts, timer 0 to SIM_TIMER_COUNT - 1. The host calls
     * sim_fw_dispatch_interrupt(SIM_TIMER_INTERRUPT_BASE + timer) every period at a scheduling
     * point (delay() included); a tick that finds the previous one still pending is lost. */
    void (*timer_start)(int32_t timer, uint64_t period_ns); /* restarts a running timer; at least 5 us */
    void (*timer_stop)(int32_t timer);
} SimHostApi;

#define SIM_TIMER_COUNT 4
#define SIM_TIMER_INTERRUPT_BASE 64

typedef enum SimResetReason {
    SIM_RESET_POWER_ON = 0,
    SIM_RESET_SOFTWARE = 1,
    SIM_RESET_DEEP_SLEEP = 2
} SimResetReason;

typedef enum SimKvType {
    SIM_KV_I8 = 1,
    SIM_KV_U8 = 2,
    SIM_KV_I16 = 3,
    SIM_KV_U16 = 4,
    SIM_KV_I32 = 5,
    SIM_KV_U32 = 6,
    SIM_KV_I64 = 7,
    SIM_KV_U64 = 8,
    SIM_KV_FLOAT = 9,
    SIM_KV_DOUBLE = 10,
    SIM_KV_BOOL = 11,
    SIM_KV_STRING = 12,
    SIM_KV_BYTES = 13
} SimKvType;

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
