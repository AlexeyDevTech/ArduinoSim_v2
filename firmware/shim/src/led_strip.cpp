// WS2812-style LED frames over the host's led_strip_write (sim/led_strip.h).
#include "sim/led_strip.h"

#include "sim/host.h"

extern "C" void sim_led_strip_write(uint8_t pin, const uint8_t* data, uint32_t length, int khz)
{
    sim::host().led_strip_write(pin, data, static_cast<int32_t>(length), khz);
}