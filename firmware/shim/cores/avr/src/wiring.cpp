// Core Arduino API: pins, time, analog, pulses, shifting, tone, interrupts, random, map.
#include <Arduino.h>

#include "sim/host.h"

using sim::host;

namespace sim {
void ports_note_pin_mode(uint8_t pin, uint8_t mode);
void ports_note_output(uint8_t pin, bool high);

namespace {
constexpr int InterruptCount = 2;
void (*g_isr[InterruptCount])(void) = {nullptr, nullptr};
bool g_interrupts_enabled = true;
uint32_t g_random_state = 1;
}  // namespace

void dispatch_interrupt(int32_t id)
{
    if (id >= 0 && id < InterruptCount && g_isr[id] != nullptr) {
        g_isr[id]();
    }
}

bool interrupts_enabled() { return g_interrupts_enabled; }

void set_interrupts_enabled(bool enabled)
{
    g_interrupts_enabled = enabled;
    host().set_interrupts_enabled(enabled ? 1 : 0);
}

// avr-libc random(): the Park-Miller "minimal standard" generator, computed with
// Schrage's method so the sequence matches a real board for the same seed.
uint32_t next_random()
{
    int32_t x = static_cast<int32_t>(g_random_state);
    if (x == 0) {
        x = 123459876;
    }
    int32_t hi = x / 127773;
    int32_t lo = x % 127773;
    x = 16807 * lo - 2836 * hi;
    if (x < 0) {
        x += 0x7FFFFFFF;
    }
    g_random_state = static_cast<uint32_t>(x);
    return static_cast<uint32_t>(x) % (static_cast<uint32_t>(RANDOM_MAX) + 1u);
}

void seed_random(uint32_t seed) { g_random_state = seed; }

}  // namespace sim

extern "C" {

void init(void) {}
void initVariant(void) {}

__attribute__((weak)) void yield(void) { host().yield(); }

void pinMode(uint8_t pin, uint8_t mode)
{
    sim::ports_note_pin_mode(pin, mode);
    host().pin_mode(pin, mode);
}

void digitalWrite(uint8_t pin, uint8_t val)
{
    sim::ports_note_output(pin, val != LOW);
    host().digital_write(pin, val != LOW ? 1 : 0);
}

int digitalRead(uint8_t pin) { return host().digital_read(pin) != 0 ? HIGH : LOW; }

int analogRead(uint8_t pin)
{
    if (pin < A0) {
        pin = static_cast<uint8_t>(pin + A0);  // analogRead(0) means A0, as on AVR
    }
    return host().analog_read(pin);
}

void analogReference(uint8_t mode) { host().analog_reference(mode); }

void analogWrite(uint8_t pin, int val)
{
    sim::ports_note_pin_mode(pin, OUTPUT);
    if (val <= 0 || val >= 255) {
        sim::ports_note_output(pin, val >= 255);
    }
    host().analog_write(pin, val);
}

unsigned long millis(void) { return static_cast<unsigned long>(host().now_us() / 1000u); }

unsigned long micros(void) { return static_cast<unsigned long>(host().now_us()); }

void delay(unsigned long ms)
{
    while (ms > 0) {
        unsigned long chunk = ms > 1000000UL ? 1000000UL : ms;
        host().delay_us(static_cast<uint32_t>(chunk * 1000UL));
        ms -= chunk;
    }
}

void delayMicroseconds(unsigned int us) { host().delay_us(us); }

void sim_delay_us(uint32_t us) { host().delay_us(us); }

void attachInterrupt(uint8_t interruptNum, void (*userFunc)(void), int mode)
{
    if (interruptNum >= sim::InterruptCount) {
        sim::log_warning("attachInterrupt: this board has external interrupts 0 (pin 2) and 1 (pin 3) only; the call is ignored");
        return;
    }
    sim::g_isr[interruptNum] = userFunc;
    host().attach_interrupt(interruptNum, mode);
}

void detachInterrupt(uint8_t interruptNum)
{
    if (interruptNum < sim::InterruptCount) {
        sim::g_isr[interruptNum] = nullptr;
        host().detach_interrupt(interruptNum);
    }
}

void sim_interrupts_enable(void) { sim::set_interrupts_enabled(true); }
void sim_interrupts_disable(void) { sim::set_interrupts_enabled(false); }

uint8_t sim_interrupts_save_disable(void)
{
    uint8_t was = sim::g_interrupts_enabled ? 1 : 0;
    sim::set_interrupts_enabled(false);
    return was;
}

uint8_t sim_interrupts_save_enable(void)
{
    uint8_t was = sim::g_interrupts_enabled ? 1 : 0;
    sim::set_interrupts_enabled(true);
    return was;
}

void sim_interrupts_restore(uint8_t enabled) { sim::set_interrupts_enabled(enabled != 0); }

void sim_sleep_cpu(void)
{
    static bool warned = false;
    if (!warned) {
        warned = true;
        sim::log_warning("sleep_cpu() is not simulated; the sketch continues immediately");
    }
    host().yield();
}

void sim_wdt_enable(int timeout)
{
    (void)timeout;
    static bool warned = false;
    if (!warned) {
        warned = true;
        sim::log_warning("the watchdog is not simulated; wdt_enable() has no effect and the board never resets");
    }
}

uint8_t sim_eeprom_read(int32_t address) { return host().eeprom_read(address); }
void sim_eeprom_write(int32_t address, uint8_t value) { host().eeprom_write(address, value); }
void sim_eeprom_update(int32_t address, uint8_t value)
{
    if (host().eeprom_read(address) != value) {
        host().eeprom_write(address, value);
    }
}

long random(void) { return static_cast<long>(sim::next_random()); }
void srandom(unsigned long seed) { sim::seed_random(static_cast<uint32_t>(seed)); }

}  // extern "C"

void tone(uint8_t pin, unsigned int frequency, unsigned long duration)
{
    sim::ports_note_pin_mode(pin, OUTPUT);
    host().tone(pin, frequency, static_cast<uint32_t>(duration));
}

void noTone(uint8_t pin) { host().no_tone(pin); }

long random(long howbig)
{
    if (howbig == 0) {
        return 0;
    }
    return static_cast<long>(sim::next_random()) % howbig;
}

long random(long howsmall, long howbig)
{
    if (howsmall >= howbig) {
        return howsmall;
    }
    return random(howbig - howsmall) + howsmall;
}

void randomSeed(unsigned long seed)
{
    if (seed != 0) {
        sim::seed_random(static_cast<uint32_t>(seed));
    }
}
