/*
 * Esp.h - the ESP object of the Arduino-ESP32 core. Values the simulator cannot measure (heap,
 * flash, sketch size) are fixed numbers of a typical ESP32-WROOM-32 with 4 MB flash; they are
 * listed in the accuracy sheet.
 */
#ifndef ESP_H
#define ESP_H

#include <stdint.h>

#include "WString.h"

class EspClass {
public:
    void restart() __attribute__((noreturn));
    void deepSleep(uint64_t time_us) __attribute__((noreturn));

    uint32_t getHeapSize();
    uint32_t getFreeHeap();
    uint32_t getMinFreeHeap();
    uint32_t getMaxAllocHeap();
    uint32_t getPsramSize() { return 0; }
    uint32_t getFreePsram() { return 0; }
    uint32_t getMinFreePsram() { return 0; }
    uint32_t getMaxAllocPsram() { return 0; }

    uint16_t getChipRevision() { return 3; }
    const char* getChipModel() { return "ESP32-D0WD-V3"; }
    uint8_t getChipCores() { return 2; }
    uint32_t getCpuFreqMHz() { return 240; }
    uint32_t getCycleCount();
    const char* getSdkVersion() { return "v5.1 (ArduSim)"; }
    const char* getCoreVersion() { return "3.0.0"; }

    uint32_t getFlashChipSize() { return 4u * 1024u * 1024u; }
    uint32_t getFlashChipSpeed() { return 40000000u; }
    uint32_t getSketchSize() { return 1310720u / 4u; }
    uint32_t getFreeSketchSpace() { return 1310720u; }
    String getSketchMD5() { return String("00000000000000000000000000000000"); }

    uint64_t getEfuseMac();
};

extern EspClass ESP;

#endif /* ESP_H */
