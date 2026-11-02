/*
 * sim/host.h - shim-internal access to the simulator's SimHostApi table.
 * Static constructors may run before sim_fw_init; host() then resolves the
 * table through the environment (see host_bootstrap.cpp).
 */
#ifndef SIM_HOST_H
#define SIM_HOST_H

#include "sim_abi.h"

namespace sim {

extern const SimHostApi* g_host;

const SimHostApi* resolve_host();
bool is_compatible(const SimHostApi* api);

inline const SimHostApi& host()
{
    const SimHostApi* api = g_host;
    if (__builtin_expect(api == nullptr, 0)) {
        api = resolve_host();
    }
    return *api;
}

inline void log_warning(const char* message) { host().log(SIM_LOG_WARNING, message); }

}  // namespace sim

#endif /* SIM_HOST_H */
