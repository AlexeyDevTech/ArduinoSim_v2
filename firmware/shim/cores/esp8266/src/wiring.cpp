// The ESP8266 Arduino core's pins, time, analog, PWM, tone, interrupts and random (core 3.x).
// PWM: any GPIO, analogWriteRange() (default 255) and analogWriteFreq() (default 1 kHz); the host
// gets the duty at 16 bits so any range maps exactly. delay() and yield() let the system run and
// feed the software watchdog; delayMicroseconds() does not.
#include <Arduino.h>
#include <stdio.h>

#include "sim/host.h"

using sim::host;

extern "C" void sim_esp8266_system(void) __attribute__((weak));

namespace sim {

namespace {

constexpr int PinCount = 17;
constexpr uint8_t AnalogPin = 17;
constexpr int32_t HostBits = 16;
constexpr uint32_t HostMax = (1u << HostBits) - 1;

struct Isr {
    void (*plain)(void);
    void (*with_arg)(void*);
    void* arg;
};

uint32_t g_range = 255;
uint32_t g_frequency = 1000;
bool g_configured[PinCount];
Isr g_isr[PinCount];
uint32_t g_random_state = 0x2545F491u;

bool valid_pin(uint8_t pin) { return pin < PinCount; }

void system_turn()
{
    host().yield();
    if (sim_esp8266_system != nullptr) {
        sim_esp8266_system();
    }
}

}  // namespace

void dispatch_interrupt(int32_t id)
{
    if (id >= 0 && id < PinCount) {
        Isr& isr = g_isr[id];
        if (isr.with_arg != nullptr) {
            isr.with_arg(isr.arg);
        } else if (isr.plain != nullptr) {
            isr.plain();
        }
    }
}

}  // namespace sim

extern "C" {

void init(void) {}
void initVariant(void) {}

void yield(void) { sim::system_turn(); }

void esp_yield(void) { sim::system_turn(); }

void optimistic_yield(uint32_t) { sim::system_turn(); }

void pinMode(uint8_t pin, uint8_t mode)
{
    int32_t sim_mode;
    switch (mode) {
    case INPUT: sim_mode = SIM_PIN_INPUT; break;
    case OUTPUT: sim_mode = SIM_PIN_OUTPUT; break;
    case INPUT_PULLUP:
    case WAKEUP_PULLUP: sim_mode = SIM_PIN_INPUT_PULLUP; break;
    case INPUT_PULLDOWN_16:
    case WAKEUP_PULLDOWN: sim_mode = SIM_PIN_INPUT_PULLDOWN; break;
    case OUTPUT_OPEN_DRAIN: sim_mode = SIM_PIN_OUTPUT_OPEN_DRAIN; break;
    default: {
        char message[128];
        snprintf(message, sizeof message, "pinMode(%u, 0x%02X): pin functions (SPECIAL, FUNCTION_n) are not simulated; the pin stays an input.", pin, mode);
        sim::log_warning(message);
        sim_mode = SIM_PIN_INPUT;
    }
    }
    if (sim::valid_pin(pin)) {
        sim::g_configured[pin] = false;
    }
    host().pin_mode(pin, sim_mode);
}

void digitalWrite(uint8_t pin, uint8_t val) { host().digital_write(pin, val != LOW ? 1 : 0); }

int digitalRead(uint8_t pin) { return host().digital_read(pin) != 0 ? HIGH : LOW; }

// As the core: A0 is the ADC (0-1023 for 0-3.3 V through the boards' divider); any other pin
// reads its digital level as 0 or 1023.
int analogRead(uint8_t pin)
{
    if (pin == sim::AnalogPin) {
        return host().analog_read(sim::AnalogPin);
    }
    return digitalRead(pin) == HIGH ? 1023 : 0;
}

void analogWriteRange(uint32_t range)
{
    if (range >= 15 && range <= 65535) {
        sim::g_range = range;
    }
}

void analogWriteResolution(int res)
{
    if (res >= 4 && res <= 16) {
        sim::g_range = (1u << res) - 1;
    }
}

void analogWriteFreq(uint32_t freq)
{
    sim::g_frequency = freq < 100 ? 100 : (freq > 60000 ? 60000 : freq);
    for (uint8_t pin = 0; pin < sim::PinCount; pin++) {
        if (sim::g_configured[pin]) {
            host().pwm_config(pin, sim::g_frequency, sim::HostBits);
        }
    }
}

void analogWrite(uint8_t pin, int val)
{
    if (!sim::valid_pin(pin)) {
        host().analog_write(pin, val);
        return;
    }
    if (!sim::g_configured[pin]) {
        host().pwm_config(pin, sim::g_frequency, sim::HostBits);
        sim::g_configured[pin] = true;
    }
    uint32_t value = val < 0 ? 0u : static_cast<uint32_t>(val);
    uint32_t duty = value >= sim::g_range ? sim::HostMax : static_cast<uint32_t>((static_cast<uint64_t>(value) * sim::HostMax) / sim::g_range);
    host().analog_write(pin, static_cast<int32_t>(duty));
}

unsigned long millis(void) { return static_cast<unsigned long>(host().now_us() / 1000u); }

unsigned long micros(void) { return static_cast<unsigned long>(host().now_us()); }

uint64_t micros64(void) { return host().now_us(); }

uint32_t system_get_time(void) { return static_cast<uint32_t>(host().now_us()); }

void delay(unsigned long ms)
{
    while (ms > 0) {
        unsigned long chunk = ms > 1000000UL ? 1000000UL : ms;
        host().delay_us(static_cast<uint32_t>(chunk * 1000UL));
        ms -= chunk;
    }
    sim::system_turn();
}

void esp_delay(unsigned long ms) { delay(ms); }

void delayMicroseconds(unsigned int us) { host().delay_us(us); }

void sim_delay_us(uint32_t us) { host().delay_us(us); }

void sim_interrupts_enable(void) { host().set_interrupts_enabled(1); }

void sim_interrupts_disable(void) { host().set_interrupts_enabled(0); }

void detachInterrupt(uint8_t pin)
{
    if (sim::valid_pin(pin)) {
        sim::g_isr[pin] = sim::Isr{nullptr, nullptr, nullptr};
        host().detach_interrupt(pin);
    }
}

uint32_t sim_random32(void)
{
    uint32_t x = sim::g_random_state;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    sim::g_random_state = x;
    return x;
}

long random(void) { return static_cast<long>(sim_random32() & 0x7FFFFFFF); }

void srandom(unsigned long seed) { sim::g_random_state = seed != 0 ? static_cast<uint32_t>(seed) : 0x2545F491u; }

uint32_t ESP_getChipId(void) { return 0x00C0FFEEu; }

}  // extern "C"

void attachInterruptArg(uint8_t pin, void (*handler)(void*), void* arg, int mode)
{
    int32_t sim_mode;
    switch (mode) {
    case RISING: sim_mode = SIM_INT_RISING; break;
    case FALLING: sim_mode = SIM_INT_FALLING; break;
    case CHANGE: sim_mode = SIM_INT_CHANGE; break;
    case ONLOW:
    case ONLOW_WE: sim_mode = SIM_INT_LOW; break;
    default:
        sim::log_warning("attachInterrupt(): ONHIGH interrupts are not simulated; the call is ignored.");
        return;
    }
    if (pin >= 16) {
        sim::log_warning("attachInterrupt(): GPIO 16 (D0) has no interrupts on the ESP8266; the call is ignored.");
        return;
    }
    sim::g_isr[pin] = sim::Isr{nullptr, handler, arg};
    host().attach_interrupt(pin, sim_mode);
}

void attachInterrupt(uint8_t pin, void (*userFunc)(void), int mode)
{
    attachInterruptArg(pin, nullptr, nullptr, mode);
    if (pin < 16) {
        sim::g_isr[pin] = sim::Isr{userFunc, nullptr, nullptr};
    }
}

void tone(uint8_t pin, unsigned int frequency, unsigned long duration) { host().tone(pin, frequency, static_cast<uint32_t>(duration)); }

void noTone(uint8_t pin) { host().no_tone(pin); }

long random(long howbig)
{
    if (howbig <= 0) {
        return 0;
    }
    return static_cast<long>(sim_random32() % static_cast<uint32_t>(howbig));
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
        srandom(seed);
    }
}

long secureRandom(long howbig) { return random(howbig); }

long secureRandom(long howsmall, long howbig) { return random(howsmall, howbig); }