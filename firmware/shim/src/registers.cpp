// Register proxies: DDRx/PORTx/PINx routed to the pin API, SREG to the
// interrupt flag, everything else warns once per register.
#include <Arduino.h>

#include <stdio.h>

#include "sim/host.h"

using sim::host;

namespace sim {
bool interrupts_enabled();
void set_interrupts_enabled(bool enabled);

namespace {

constexpr int PortCount = 3;
// Pins of each port by bit (-1: bit not wired to a board pin). B: D8-D13, C: A0-A5, D: D0-D7.
constexpr int8_t PortPins[PortCount][8] = {
    {8, 9, 10, 11, 12, 13, -1, -1},
    {14, 15, 16, 17, 18, 19, -1, -1},
    {0, 1, 2, 3, 4, 5, 6, 7},
};

uint8_t g_output[PortCount];
uint8_t g_direction[PortCount];
uint32_t g_warned[4];

bool locate(uint8_t pin, uint8_t& port, uint8_t& bit)
{
    if (pin < 8) {
        port = PortD;
        bit = pin;
    } else if (pin < 14) {
        port = PortB;
        bit = static_cast<uint8_t>(pin - 8);
    } else if (pin < 20) {
        port = PortC;
        bit = static_cast<uint8_t>(pin - 14);
    } else {
        return false;
    }
    return true;
}

void set_bit(uint8_t& value, uint8_t bit, bool on)
{
    value = on ? static_cast<uint8_t>(value | (1u << bit)) : static_cast<uint8_t>(value & ~(1u << bit));
}

}  // namespace

void ports_note_pin_mode(uint8_t pin, uint8_t mode)
{
    uint8_t port;
    uint8_t bit;
    if (!locate(pin, port, bit)) {
        return;
    }
    set_bit(g_direction[port], bit, mode == OUTPUT);
    if (mode == INPUT) {
        set_bit(g_output[port], bit, false);
    } else if (mode == INPUT_PULLUP) {
        set_bit(g_output[port], bit, true);
    }
}

void ports_note_output(uint8_t pin, bool high)
{
    uint8_t port;
    uint8_t bit;
    if (locate(pin, port, bit)) {
        set_bit(g_output[port], bit, high);
    }
}

uint8_t port_register_read(uint8_t port, uint8_t kind)
{
    if (port >= PortCount) {
        return 0;
    }
    if (kind == PortOutput) {
        return g_output[port];
    }
    if (kind == PortDirection) {
        return g_direction[port];
    }
    uint8_t value = 0;
    for (uint8_t bit = 0; bit < 8; bit++) {
        int8_t pin = PortPins[port][bit];
        if (pin >= 0 && host().digital_read(pin) != 0) {
            value = static_cast<uint8_t>(value | (1u << bit));
        }
    }
    return value;
}

void port_register_write(uint8_t port, uint8_t kind, uint8_t value)
{
    if (port >= PortCount) {
        return;
    }
    if (kind == PortInput) {
        // Writing 1 to a PINx bit toggles the PORTx bit, as on the ATmega328P.
        kind = PortOutput;
        value = static_cast<uint8_t>(g_output[port] ^ value);
    }

    uint8_t changed;
    if (kind == PortOutput) {
        changed = static_cast<uint8_t>(g_output[port] ^ value);
        g_output[port] = value;
    } else {
        changed = static_cast<uint8_t>(g_direction[port] ^ value);
        g_direction[port] = value;
    }

    for (uint8_t bit = 0; bit < 8; bit++) {
        int8_t pin = PortPins[port][bit];
        if (pin < 0 || (changed & (1u << bit)) == 0) {
            continue;
        }
        bool output = (g_direction[port] & (1u << bit)) != 0;
        bool high = (g_output[port] & (1u << bit)) != 0;
        if (kind == PortOutput) {
            host().digital_write(pin, high ? 1 : 0);  // on an input pin this switches the pull-up
        } else {
            host().pin_mode(pin, output ? SIM_PIN_OUTPUT : (high ? SIM_PIN_INPUT_PULLUP : SIM_PIN_INPUT));
        }
    }
}

void unsimulated_register_written(uint8_t id, const char* name)
{
    uint32_t mask = 1u << (id % 32);
    if ((g_warned[id / 32] & mask) != 0) {
        return;
    }
    g_warned[id / 32] |= mask;
    char message[160];
    snprintf(message, sizeof message, "register %s is not simulated; writes have no effect and it reads as 0", name);
    log_warning(message);
}

uint8_t status_register_read() { return interrupts_enabled() ? 0x80 : 0x00; }

void status_register_write(uint8_t value) { set_interrupts_enabled((value & 0x80) != 0); }

}  // namespace sim
