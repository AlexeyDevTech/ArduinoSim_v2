// The ESP object and the ESP-IDF system calls of the ESP32 core (see Esp.h).
#include <Arduino.h>

#include "sim/host.h"

using sim::host;

EspClass ESP;

namespace {

constexpr uint32_t HeapSize = 327680;
constexpr uint32_t FreeHeap = 290000;

uint64_t g_wakeup_us = 0;

}  // namespace

extern "C" {

void esp_restart(void)
{
    Serial.flush();
    host().restart();
    for (;;) {
    }
}

esp_reset_reason_t esp_reset_reason(void)
{
    switch (host().reset_reason()) {
    case SIM_RESET_SOFTWARE: return ESP_RST_SW;
    case SIM_RESET_DEEP_SLEEP: return ESP_RST_DEEPSLEEP;
    default: return ESP_RST_POWERON;
    }
}

esp_sleep_wakeup_cause_t esp_sleep_get_wakeup_cause(void)
{
    return host().reset_reason() == SIM_RESET_DEEP_SLEEP ? ESP_SLEEP_WAKEUP_TIMER : ESP_SLEEP_WAKEUP_UNDEFINED;
}

int esp_sleep_enable_timer_wakeup(uint64_t time_in_us)
{
    g_wakeup_us = time_in_us;
    return 0;
}

void esp_deep_sleep_start(void)
{
    if (g_wakeup_us == 0) {
        sim::log_warning("esp_deep_sleep_start(): no timer wake-up is set; only timer wake-ups are simulated, so the board sleeps for an hour.");
        g_wakeup_us = 3600ull * 1000000ull;
    }
    Serial.flush();
    host().deep_sleep(g_wakeup_us);
    for (;;) {
    }
}

void esp_deep_sleep(uint64_t time_in_us)
{
    esp_sleep_enable_timer_wakeup(time_in_us);
    esp_deep_sleep_start();
}

uint32_t esp_get_free_heap_size(void) { return FreeHeap; }

uint32_t esp_get_minimum_free_heap_size(void) { return FreeHeap - 8192; }

}  // extern "C"

void EspClass::restart() { esp_restart(); }

// Deep sleep: the MCU is off for the given time, then it boots (wake-up by timer only).
void EspClass::deepSleep(uint64_t time_us) { esp_deep_sleep(time_us); }

uint32_t EspClass::getHeapSize() { return HeapSize; }

uint32_t EspClass::getFreeHeap() { return FreeHeap; }

uint32_t EspClass::getMinFreeHeap() { return FreeHeap - 8192; }

uint32_t EspClass::getMaxAllocHeap() { return 110580; }

// The CPU cycle counter at 240 MHz, from virtual time.
uint32_t EspClass::getCycleCount() { return static_cast<uint32_t>(host().now_us() * 240u); }

// The factory MAC 24:0A:C4:00:00:01 as the core returns it (byte 0 in the lowest bits).
uint64_t EspClass::getEfuseMac() { return 0x010000C40A24ull; }
