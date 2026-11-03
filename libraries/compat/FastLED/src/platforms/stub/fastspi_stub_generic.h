// ArduSim (libraries/compat/FastLED/README.md): FastLED 3.9.20's stub SPI output, which drops the
// data of SPI LED chipsets (APA102, WS2801, ...); here it says so once, as the simulator has no
// part for them.
#pragma once

#ifndef FASTLED_STUB_IMPL
#error "why is this being included?"
#endif

#include <stdint.h>
#include <sim/host.h>
#include "fl/namespace.h"
#include "fl/unused.h"

// Signal to the engine that all pins are hardware SPI
#define FASTLED_ALL_PINS_HARDWARE_SPI

FASTLED_NAMESPACE_BEGIN

class StubSPIOutput {
public:
    StubSPIOutput() { }
    void select() { }
    void init() {
        static bool warned = false;
        if (!warned) {
            warned = true;
            sim::log_warning("FastLED: SPI LED chipsets (APA102, WS2801 and the like) are not simulated; their data goes nowhere. WS2812-style (clockless) strips work.");
        }
    }
    void waitFully() {}
    void release() {}
    void writeByte(uint8_t byte) { FASTLED_UNUSED(byte); }
    void writeWord(uint16_t word) { FASTLED_UNUSED(word); }
};


FASTLED_NAMESPACE_END