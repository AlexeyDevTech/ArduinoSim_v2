// firmware.dll exports (docs/adr/ADR-001-simulation-approach.md).
#include <Arduino.h>

#include "sim/host.h"

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
    return 0;
}

SIM_FW_EXPORT void sim_fw_setup(void) { setup(); }

SIM_FW_EXPORT void sim_fw_loop(void)
{
    loop();
    if (serialEvent != nullptr && Serial.available() > 0) {
        serialEvent();
    }
}

SIM_FW_EXPORT void sim_fw_dispatch_interrupt(int32_t id) { sim::dispatch_interrupt(id); }

}  // extern "C"
