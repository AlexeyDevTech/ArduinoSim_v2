// Locates the simulator's SimHostApi. The only shim file that includes <windows.h>.
#include "sim/host.h"

#include <stdio.h>
#include <stdlib.h>

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

namespace sim {

const SimHostApi* g_host = nullptr;

bool is_compatible(const SimHostApi* api)
{
    return api != nullptr && (api->abi_version >> 16) == SIM_ABI_VERSION_MAJOR &&
           (api->abi_version & 0xFFFFu) >= SIM_ABI_VERSION_MINOR && api->struct_size >= sizeof(SimHostApi);
}

// Called on the first host call when it happens before sim_fw_init, i.e. from a
// C++ static constructor while ArduSim.Runner is still inside LoadLibrary.
const SimHostApi* resolve_host()
{
    char text[32];
    DWORD n = GetEnvironmentVariableA(SIM_HOST_API_ENV, text, sizeof text);
    if (n == 0 || n >= sizeof text) {
        fprintf(stderr, "ArduSim firmware: no simulator host (%s is not set). Run the firmware through ArduSim.Runner.\n", SIM_HOST_API_ENV);
        abort();
    }

    const SimHostApi* api = reinterpret_cast<const SimHostApi*>(static_cast<uintptr_t>(strtoull(text, nullptr, 16)));
    if (!is_compatible(api)) {
        fprintf(stderr, "ArduSim firmware: the simulator host is incompatible with firmware ABI %u.%u. Rebuild the sketch.\n",
                static_cast<unsigned>(SIM_ABI_VERSION_MAJOR), static_cast<unsigned>(SIM_ABI_VERSION_MINOR));
        abort();
    }

    g_host = api;
    return api;
}

}  // namespace sim
