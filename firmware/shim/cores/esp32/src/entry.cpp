// firmware.dll exports for the ESP32 core (docs/adr/ADR-001-simulation-approach.md, ADR-009).
// setup() and loop() run in the loopTask; after each loop() the other due tasks get their turn.
#include <Arduino.h>
#include <stdarg.h>
#include <stdio.h>

#include "sim/host.h"
#include "sim/timers.h"

void serialEvent() __attribute__((weak));

namespace sim {
void dispatch_interrupt(int32_t id);
}

extern "C" {

SIM_FW_EXPORT uint32_t sim_fw_abi_version(void) { return SIM_ABI_VERSION; }

SIM_FW_EXPORT int32_t sim_fw_init(const SimHostApi* host)
{
    if (!sim::is_compatible(host)) {
        return 1;
    }
    sim::g_host = host;
    sim::install_crash_handler(host->crash_info);
    return 0;
}

SIM_FW_EXPORT void sim_fw_setup(void) { setup(); }

SIM_FW_EXPORT void sim_fw_loop(void)
{
    loop();
    if (serialEvent != nullptr && Serial.available() > 0) {
        serialEvent();
    }
    sim_task_switch_point();
}

SIM_FW_EXPORT void sim_fw_dispatch_interrupt(int32_t id)
{
    if (!sim::dispatch_timer(id)) {
        sim::dispatch_interrupt(id);
    }
}

void sim_log_printf(const char* format, ...)
{
    char text[256];
    va_list args;
    va_start(args, format);
    vsnprintf(text, sizeof text, format, args);
    va_end(args);
    Serial.print(text);
}

}  // extern "C"