// firmware.dll exports for the ESP8266 core (docs/adr/ADR-001-simulation-approach.md, ADR-009).
// After each loop() the system gets its turn, as the core's loop_wrapper yields to the SDK.
#include <Arduino.h>

#include "sim/host.h"
#include "sim/timers.h"

void serialEvent() __attribute__((weak));

namespace sim {
void dispatch_interrupt(int32_t id);
}

extern "C" void sim_esp8266_system(void) __attribute__((weak));

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
    if (sim_esp8266_system != nullptr) {
        sim_esp8266_system();
    }
}

SIM_FW_EXPORT void sim_fw_dispatch_interrupt(int32_t id)
{
    if (!sim::dispatch_timer(id)) {
        sim::dispatch_interrupt(id);
    }
}

}  // extern "C"