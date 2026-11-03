// The ESP object and the ESP-IDF system calls of the ESP32 core (see Esp.h).
#include <Arduino.h>

#include "sim/host.h"

using sim::host;

EspClass ESP;

namespace {

constexpr uint32_t HeapSize = 327680;
constexpr uint32_t FreeHeap = 290000;

}  // namespace

extern "C" {

void esp_restart(void)
{
    Serial.flush();
    host().restart();
    for (;;) {
    }
}

uint32_t esp_get_free_heap_size(void) { return FreeHeap; }

uint32_t esp_get_minimum_free_heap_size(void) { return FreeHeap - 8192; }

}  // extern "C"

void EspClass::restart() { esp_restart(); }

// Deep sleep: nothing runs for the given time, then the board resets (wake-up by timer only).
void EspClass::deepSleep(uint64_t time_us)
{
    sim::log_warning("ESP.deepSleep(): the board sleeps for the given time and then restarts; other wake-up sources are not simulated.");
    Serial.flush();
    while (time_us > 0) {
        uint32_t chunk = time_us > 1000000000ull ? 1000000000u : static_cast<uint32_t>(time_us);
        host().delay_us(chunk);
        time_us -= chunk;
    }
    host().restart();
    for (;;) {
    }
}

uint32_t EspClass::getHeapSize() { return HeapSize; }

uint32_t EspClass::getFreeHeap() { return FreeHeap; }

uint32_t EspClass::getMinFreeHeap() { return FreeHeap - 8192; }

uint32_t EspClass::getMaxAllocHeap() { return 110580; }

// The CPU cycle counter at 240 MHz, from virtual time.
uint32_t EspClass::getCycleCount() { return static_cast<uint32_t>(host().now_us() * 240u); }

// The factory MAC 24:0A:C4:00:00:01 as the core returns it (byte 0 in the lowest bits).
uint64_t EspClass::getEfuseMac() { return 0x010000C40A24ull; }
