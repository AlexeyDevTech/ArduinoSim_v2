/*
 * Esp.h - the ESP object of the ESP8266 Arduino core. Values the simulator cannot measure (heap,
 * flash, sketch size) are fixed numbers of a typical ESP-12E with 4 MB flash; getVcc() reports the
 * simulated 3.3 V supply. RTC user memory is not simulated.
 */
#ifndef ESP_H
#define ESP_H

#include <stddef.h>
#include <stdint.h>

#include "WString.h"

enum RFMode { RF_DEFAULT = 0, RF_CAL = 1, RF_NO_CAL = 2, RF_DISABLED = 4 };
#define WAKE_RF_DEFAULT RF_DEFAULT
#define WAKE_RFCAL RF_CAL
#define WAKE_NO_RFCAL RF_NO_CAL
#define WAKE_RF_DISABLED RF_DISABLED

class EspClass {
public:
    void wdtEnable(uint32_t timeout_ms = 0);
    void wdtDisable();
    void wdtFeed();

    void deepSleep(uint64_t time_us, RFMode mode = RF_DEFAULT) __attribute__((noreturn));
    void deepSleepInstant(uint64_t time_us, RFMode mode = RF_DEFAULT) __attribute__((noreturn)) { deepSleep(time_us, mode); }
    uint64_t deepSleepMax() { return 3600000000ull * 3; }
    bool rtcUserMemoryRead(uint32_t offset, uint32_t* data, size_t size) __attribute__((unavailable("ArduSim does not simulate the ESP8266 RTC user memory")));
    bool rtcUserMemoryWrite(uint32_t offset, uint32_t* data, size_t size) __attribute__((unavailable("ArduSim does not simulate the ESP8266 RTC user memory")));

    void reset() __attribute__((noreturn));
    void restart() __attribute__((noreturn));

    uint16_t getVcc() { return 3300; }
    uint32_t getChipId() { return 0x00C0FFEEu; }
    uint32_t getFreeHeap() { return 45000; }
    uint16_t getMaxFreeBlockSize() { return 40000; }
    uint8_t getHeapFragmentation() { return 5; }
    void getHeapStats(uint32_t* free = nullptr, uint16_t* max = nullptr, uint8_t* frag = nullptr);
    uint32_t getFreeContStack() { return 3500; }
    const char* getSdkVersion() { return "2.2.2-dev(ArduSim)"; }
    String getCoreVersion() { return String("3.1.2"); }
    String getFullVersion() { return String("SDK:2.2.2-dev(ArduSim)/Core:3.1.2"); }
    uint8_t getBootVersion() { return 31; }
    uint8_t getBootMode() { return 1; }
    uint8_t getCpuFreqMHz() { return 80; }
    uint32_t getFlashChipId() { return 0x1640EF; }
    uint8_t getFlashChipVendorId() { return 0xEF; }
    uint32_t getFlashChipRealSize() { return 4u * 1024u * 1024u; }
    uint32_t getFlashChipSize() { return 4u * 1024u * 1024u; }
    uint32_t getFlashChipSpeed() { return 40000000u; }
    uint32_t getSketchSize() { return 300000u; }
    uint32_t getFreeSketchSpace() { return 1044480u; }
    String getSketchMD5() { return String("00000000000000000000000000000000"); }
    String getResetReason();
    String getResetInfo() { return String("Fatal exception:0 flag:") + getResetReason(); }
    uint32_t getCycleCount();
    bool checkFlashCRC() { return true; }
};

extern EspClass ESP;

#endif /* ESP_H */