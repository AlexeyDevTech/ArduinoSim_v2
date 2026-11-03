// The ESP object of the ESP8266 core (see Esp.h).
#include <Arduino.h>

#include "sim/host.h"

using sim::host;

EspClass ESP;

void EspClass::wdtEnable(uint32_t) {}

void EspClass::wdtDisable()
{
    static bool warned = false;
    if (!warned) {
        warned = true;
        sim::log_warning("ESP.wdtDisable(): only the software watchdog stops on a real board; the hardware watchdog still resets it after about 8 s. The simulation only warns about the software watchdog.");
    }
}

void EspClass::wdtFeed() { host().yield(); }

// Deep sleep: the MCU is off for the given time, then it boots. On the board GPIO 16 (D0) must be
// wired to RST for the timer to wake it.
void EspClass::deepSleep(uint64_t time_us, RFMode)
{
    Serial.flush();
    host().deep_sleep(time_us);
    for (;;) {
    }
}

void EspClass::reset() { restart(); }

void EspClass::restart()
{
    Serial.flush();
    host().restart();
    for (;;) {
    }
}

void EspClass::getHeapStats(uint32_t* free, uint16_t* max, uint8_t* frag)
{
    if (free != nullptr) {
        *free = getFreeHeap();
    }
    if (max != nullptr) {
        *max = getMaxFreeBlockSize();
    }
    if (frag != nullptr) {
        *frag = getHeapFragmentation();
    }
}

String EspClass::getResetReason()
{
    switch (host().reset_reason()) {
    case SIM_RESET_SOFTWARE: return String("Software/System restart");
    case SIM_RESET_DEEP_SLEEP: return String("Deep-Sleep Wake");
    default: return String("Power On");
    }
}

// The CPU cycle counter at 80 MHz, from virtual time.
uint32_t EspClass::getCycleCount() { return static_cast<uint32_t>(host().now_us() * 80u); }