// Toolchain smoke test: a stand-in for firmware.dll that exercises the draft
// ABI (firmware/shim/include/sim_abi.h) and the C++ runtime features the
// Arduino shim relies on: virtual dispatch, templates, operator new, static
// constructors, snprintf with floating point.
#include "sim_abi.h"

#include <stdio.h>
#include <stdlib.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

namespace {

const SimHostApi* g_host = nullptr;

// Static constructors run during LoadLibrary, before sim_fw_init. They find
// the host through the environment variable the Runner sets before loading.
const SimHostApi* host() {
    if (g_host == nullptr) {
        char text[32];
        DWORD n = GetEnvironmentVariableA(SIM_HOST_API_ENV, text, sizeof text);
        if (n > 0 && n < sizeof text) {
            g_host = reinterpret_cast<const SimHostApi*>(strtoull(text, nullptr, 16));
        }
    }
    return g_host;
}

class Print {
public:
    virtual ~Print() = default;
    virtual size_t write(uint8_t b) = 0;
    size_t print(const char* s) {
        size_t n = 0;
        while (*s != '\0') {
            n += write(static_cast<uint8_t>(*s++));
        }
        return n;
    }
    size_t print(double v) {
        char buf[32];
        snprintf(buf, sizeof buf, "%.2f", v);
        return print(buf);
    }
};

class SerialPort : public Print {
public:
    size_t write(uint8_t b) override { return static_cast<size_t>(host()->serial_write(0, &b, 1)); }
};

template <typename T, int N>
struct Ring {
    T items[N] = {};
    int head = 0;
    void push(T v) {
        items[head] = v;
        head = (head + 1) % N;
    }
};

struct EarlyBird {
    EarlyBird() { host()->pin_mode(13, SIM_PIN_OUTPUT); }
};

SerialPort Serial;
EarlyBird early_bird;
Ring<int, 8>* ring = nullptr;
int counter = 0;

}  // namespace

extern "C" {

SIM_FW_EXPORT uint32_t sim_fw_abi_version(void) { return SIM_ABI_VERSION; }

SIM_FW_EXPORT int32_t sim_fw_init(const SimHostApi* api) {
    if (api == nullptr || (api->abi_version >> 16) != SIM_ABI_VERSION_MAJOR ||
        api->struct_size < sizeof(SimHostApi)) {
        return 1;
    }
    g_host = api;
    return 0;
}

SIM_FW_EXPORT void sim_fw_setup(void) {
    ring = new Ring<int, 8>();
    Serial.print("smoke setup ");
    Serial.print(3.14159);
    Serial.print("\n");
}

SIM_FW_EXPORT void sim_fw_loop(void) {
    for (int i = 0; i < 1000; ++i) {
        host()->digital_write(13, (counter + i) & 1);
    }
    ring->push(counter++);
}

SIM_FW_EXPORT void sim_fw_dispatch_interrupt(int32_t id) { (void)id; }

}  // extern "C"
