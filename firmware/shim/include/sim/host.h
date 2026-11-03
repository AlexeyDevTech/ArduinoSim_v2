/*
 * sim/host.h - shim-internal access to the simulator's SimHostApi table.
 * Static constructors may run before sim_fw_init; host() then resolves the
 * table through the environment (see host_bootstrap.cpp).
 */
#ifndef SIM_HOST_H
#define SIM_HOST_H

#include <stddef.h>

#include "sim_abi.h"

namespace sim {

extern const SimHostApi* g_host;

const SimHostApi* resolve_host();
bool is_compatible(const SimHostApi* api);

/* Records fatal exceptions in firmware.dll (or in the C runtime it calls) into crash_info. */
void install_crash_handler(SimCrashInfo* crash);

/* Threads and signals for cooperative tasks (the ESP32 core's FreeRTOS): every task is an OS
 * thread, but exactly one runs at a time, handing over with signals, so host calls stay
 * serialized as on one simulation thread. Wrapped here so that only host_bootstrap.cpp sees
 * <windows.h>. A signal is an auto-reset event. */
void* signal_create();
void signal_set(void* signal);
void signal_wait(void* signal);
bool thread_start(void (*entry)(void*), void* argument, size_t stack_bytes);

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
