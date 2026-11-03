// Arduino API shared by every core: pulses, shifting, map, makeWord (docs/ARCHITECTURE.md, "Firmware shim").
#include <Arduino.h>

#include "sim/host.h"

using sim::host;

extern "C" {
unsigned long pulseInLong(uint8_t pin, uint8_t state, unsigned long timeout)
{
    const uint64_t start = host().now_us();
    const int level = state != LOW ? 1 : 0;
    auto expired = [&]() { return host().now_us() - start >= timeout; };
    while (host().digital_read(pin) == level) {  // let a pulse already in progress finish
        if (expired()) {
            return 0;
        }
    }
    while (host().digital_read(pin) != level) {
        if (expired()) {
            return 0;
        }
    }
    const uint64_t begin = host().now_us();
    while (host().digital_read(pin) == level) {
        if (expired()) {
            return 0;
        }
    }
    return static_cast<unsigned long>(host().now_us() - begin);
}

unsigned long pulseIn(uint8_t pin, uint8_t state, unsigned long timeout) { return pulseInLong(pin, state, timeout); }

void shiftOut(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder, uint8_t val)
{
    for (uint8_t i = 0; i < 8; i++) {
        uint8_t bit = bitOrder == LSBFIRST ? (val >> i) & 1 : (val >> (7 - i)) & 1;
        digitalWrite(dataPin, bit);
        digitalWrite(clockPin, HIGH);
        digitalWrite(clockPin, LOW);
    }
}

uint8_t shiftIn(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder)
{
    uint8_t value = 0;
    for (uint8_t i = 0; i < 8; i++) {
        digitalWrite(clockPin, HIGH);
        uint8_t bit = static_cast<uint8_t>(digitalRead(dataPin));
        value |= bitOrder == LSBFIRST ? static_cast<uint8_t>(bit << i) : static_cast<uint8_t>(bit << (7 - i));
        digitalWrite(clockPin, LOW);
    }
    return value;
}

}  // extern "C"

long map(long value, long fromLow, long fromHigh, long toLow, long toHigh)
{
    return (value - fromLow) * (toHigh - toLow) / (fromHigh - fromLow) + toLow;
}

uint16_t makeWord(uint16_t w) { return w; }
uint16_t makeWord(uint8_t h, uint8_t l) { return static_cast<uint16_t>((h << 8) | l); }
