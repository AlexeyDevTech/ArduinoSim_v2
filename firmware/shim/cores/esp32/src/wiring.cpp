// The Arduino-ESP32 hardware layer: pins, time, ADC, LEDC PWM, DAC, tone, interrupts, random
// (see esp32-hal.h). Pin numbers are GPIO numbers; the board's 12-bit ADC and LEDC are modelled
// by the host, the core's scaling and channel bookkeeping are done here as the core does them.
#include <Arduino.h>
#include <stdio.h>

#include "freertos/FreeRTOS.h"
#include "sim/host.h"

using sim::host;

namespace sim {

namespace {

constexpr int PinCount = 40;
constexpr int ChannelCount = 16;
constexpr uint8_t NativeAdcBits = 12;

struct PwmPin {
    bool attached;
    uint32_t frequency;
    uint8_t resolution;
    uint32_t duty;
    int8_t channel;
};

struct Channel {
    bool configured;
    uint32_t frequency;
    uint8_t resolution;
};

struct Isr {
    void (*plain)(void);
    void (*with_arg)(void*);
    void* arg;
};

PwmPin g_pwm[PinCount];
Channel g_channels[ChannelCount];
bool g_legacy_channels = false;  // ledcSetup() was used: ledcWrite() then takes a channel (core 2.x)
Isr g_isr[PinCount];
uint8_t g_adc_bits = NativeAdcBits;
uint32_t g_random_state = 0x9E3779B9u;

bool valid_pin(uint8_t pin) { return pin < PinCount; }

void warn_once(bool& flag, const char* message)
{
    if (!flag) {
        flag = true;
        log_warning(message);
    }
}

int32_t sim_mode(uint8_t mode)
{
    switch (mode) {
    case INPUT:
    case ANALOG:
        return SIM_PIN_INPUT;
    case OUTPUT:
        return SIM_PIN_OUTPUT;
    case INPUT_PULLUP:
    case PULLUP:
        return SIM_PIN_INPUT_PULLUP;
    case INPUT_PULLDOWN:
    case PULLDOWN:
        return SIM_PIN_INPUT_PULLDOWN;
    case OUTPUT_OPEN_DRAIN:
    case OPEN_DRAIN:
        return SIM_PIN_OUTPUT_OPEN_DRAIN;
    default:
        return -1;
    }
}

bool attach(uint8_t pin, uint32_t frequency, uint8_t resolution, int8_t channel)
{
    if (!valid_pin(pin)) {
        return false;
    }
    g_pwm[pin] = PwmPin{true, frequency, resolution, 0, channel};
    host().pwm_config(pin, frequency, resolution);
    return true;
}

void write_duty(uint8_t pin, uint32_t duty)
{
    g_pwm[pin].duty = duty;
    host().analog_write(pin, static_cast<int32_t>(duty));
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

__attribute__((weak)) void yield(void) { sim_task_yield(); }

void pinMode(uint8_t pin, uint8_t mode)
{
    int32_t sim_mode = sim::sim_mode(mode);
    if (sim_mode < 0) {
        char message[80];
        snprintf(message, sizeof message, "pinMode(%u, 0x%02X): unknown mode; the pin stays an input.", pin, mode);
        sim::log_warning(message);
        sim_mode = SIM_PIN_INPUT;
    }
    if (sim::valid_pin(pin)) {
        sim::g_pwm[pin].attached = false;
    }
    host().pin_mode(pin, sim_mode);
}

void digitalWrite(uint8_t pin, uint8_t val) { host().digital_write(pin, val != LOW ? 1 : 0); }

int digitalRead(uint8_t pin) { return host().digital_read(pin) != 0 ? HIGH : LOW; }

int analogRead(uint8_t pin)
{
    int32_t raw = host().analog_read(pin);
    if (sim::g_adc_bits == sim::NativeAdcBits) {
        return raw;
    }
    return sim::g_adc_bits < sim::NativeAdcBits ? raw >> (sim::NativeAdcBits - sim::g_adc_bits) : raw << (sim::g_adc_bits - sim::NativeAdcBits);
}

void analogReadResolution(uint8_t bits)
{
    if (bits < 1 || bits > 16) {
        sim::log_warning("analogReadResolution(): bits must be 1..16; the resolution stays as it was.");
        return;
    }
    sim::g_adc_bits = bits;
}

void analogSetWidth(uint8_t bits) { analogReadResolution(bits); }

// The ADC is modelled as linear 0-3.3 V (no attenuation curve, no calibration).
uint32_t analogReadMilliVolts(uint8_t pin) { return static_cast<uint32_t>(host().analog_read(pin)) * 3300u / 4095u; }

void analogSetAttenuation(adc_attenuation_t)
{
    static bool warned = false;
    sim::warn_once(warned, "analogSetAttenuation(): the ADC is simulated as linear 0-3.3 V for every attenuation.");
}

void analogSetPinAttenuation(uint8_t, adc_attenuation_t attenuation) { analogSetAttenuation(attenuation); }

int8_t digitalPinToAnalogChannel(uint8_t pin)
{
    switch (pin) {
    case 36: return 0;
    case 37: return 1;
    case 38: return 2;
    case 39: return 3;
    case 32: return 4;
    case 33: return 5;
    case 34: return 6;
    case 35: return 7;
    case 4: return 10;
    case 0: return 11;
    case 2: return 12;
    case 15: return 13;
    case 13: return 14;
    case 12: return 15;
    case 14: return 16;
    case 27: return 17;
    case 25: return 18;
    case 26: return 19;
    default: return -1;
    }
}

int8_t analogChannelToDigitalPin(uint8_t channel)
{
    static const int8_t pins[20] = {36, 37, 38, 39, 32, 33, 34, 35, -1, -1, 4, 0, 2, 15, 13, 12, 14, 27, 25, 26};
    return channel < 20 ? pins[channel] : -1;
}

// analogWrite on the ESP32 core 3.x: LEDC at 1 kHz with 8 bits unless set otherwise.
void analogWrite(uint8_t pin, int val)
{
    if (!sim::valid_pin(pin)) {
        host().analog_write(pin, val);
        return;
    }
    if (!sim::g_pwm[pin].attached) {
        sim::attach(pin, 1000, 8, -1);
    }
    sim::write_duty(pin, val < 0 ? 0u : static_cast<uint32_t>(val));
}

void analogWriteResolution(uint8_t pin, uint8_t resolution)
{
    if (sim::valid_pin(pin)) {
        sim::attach(pin, sim::g_pwm[pin].attached ? sim::g_pwm[pin].frequency : 1000, resolution, sim::g_pwm[pin].channel);
    }
}

void analogWriteFrequency(uint8_t pin, uint32_t freq)
{
    if (sim::valid_pin(pin)) {
        sim::attach(pin, freq, sim::g_pwm[pin].attached ? sim::g_pwm[pin].resolution : 8, sim::g_pwm[pin].channel);
    }
}

bool ledcAttach(uint8_t pin, uint32_t freq, uint8_t resolution) { return sim::attach(pin, freq, resolution, -1); }

bool ledcAttachChannel(uint8_t pin, uint32_t freq, uint8_t resolution, uint8_t channel)
{
    return channel < sim::ChannelCount && sim::attach(pin, freq, resolution, static_cast<int8_t>(channel));
}

bool ledcWrite(uint8_t pinOrChannel, uint32_t duty)
{
    if (sim::g_legacy_channels && pinOrChannel < sim::ChannelCount) {
        bool any = false;
        for (uint8_t pin = 0; pin < sim::PinCount; pin++) {
            if (sim::g_pwm[pin].attached && sim::g_pwm[pin].channel == pinOrChannel) {
                sim::write_duty(pin, duty);
                any = true;
            }
        }
        return any;
    }

    if (!sim::valid_pin(pinOrChannel) || !sim::g_pwm[pinOrChannel].attached) {
        static bool warned = false;
        sim::warn_once(warned, "ledcWrite(): the pin is not attached to LEDC; call ledcAttach(pin, freq, resolution) first.");
        return false;
    }
    sim::write_duty(pinOrChannel, duty);
    return true;
}

uint32_t ledcWriteTone(uint8_t pin, uint32_t freq)
{
    if (freq == 0) {
        host().no_tone(pin);
    } else {
        host().tone(pin, freq, 0);
    }
    return freq;
}

uint32_t ledcWriteNote(uint8_t pin, note_t note, uint8_t octave)
{
    static const uint16_t base[12] = {4186, 4435, 4699, 4978, 5274, 5588, 5920, 6272, 6645, 7040, 7459, 7902};
    if (note >= NOTE_MAX || octave > 8) {
        return 0;
    }
    return ledcWriteTone(pin, base[note] / (1u << (8 - octave)));
}

uint32_t ledcRead(uint8_t pin) { return sim::valid_pin(pin) ? sim::g_pwm[pin].duty : 0; }

uint32_t ledcReadFreq(uint8_t pin) { return sim::valid_pin(pin) && sim::g_pwm[pin].attached ? sim::g_pwm[pin].frequency : 0; }

bool ledcDetach(uint8_t pin)
{
    if (!sim::valid_pin(pin) || !sim::g_pwm[pin].attached) {
        return false;
    }
    sim::write_duty(pin, 0);
    sim::g_pwm[pin].attached = false;
    return true;
}

uint32_t ledcChangeFrequency(uint8_t pin, uint32_t freq, uint8_t resolution)
{
    return sim::valid_pin(pin) && sim::g_pwm[pin].attached && sim::attach(pin, freq, resolution, sim::g_pwm[pin].channel) ? freq : 0;
}

uint32_t ledcSetup(uint8_t channel, uint32_t freq, uint8_t resolution)
{
    if (channel >= sim::ChannelCount) {
        return 0;
    }
    sim::g_legacy_channels = true;
    sim::g_channels[channel] = sim::Channel{true, freq, resolution};
    for (uint8_t pin = 0; pin < sim::PinCount; pin++) {
        if (sim::g_pwm[pin].attached && sim::g_pwm[pin].channel == channel) {
            sim::attach(pin, freq, resolution, static_cast<int8_t>(channel));
        }
    }
    return freq;
}

void ledcAttachPin(uint8_t pin, uint8_t channel)
{
    if (channel < sim::ChannelCount) {
        const sim::Channel& c = sim::g_channels[channel];
        sim::attach(pin, c.configured ? c.frequency : 5000, c.configured ? c.resolution : 8, static_cast<int8_t>(channel));
    }
}

void ledcDetachPin(uint8_t pin) { ledcDetach(pin); }

void dacWrite(uint8_t pin, uint8_t value) { host().dac_write(pin, value); }

void dacDisable(uint8_t pin) { host().pin_mode(pin, SIM_PIN_INPUT); }

unsigned long millis(void) { return static_cast<unsigned long>(host().now_us() / 1000u); }

unsigned long micros(void) { return static_cast<unsigned long>(host().now_us()); }

int64_t esp_timer_get_time(void) { return static_cast<int64_t>(host().now_us()); }

// delay() lets other tasks run, as vTaskDelay on the real core; delayMicroseconds() busy-waits.
void delay(unsigned long ms) { vTaskDelay(static_cast<TickType_t>(ms)); }

void delayMicroseconds(unsigned int us) { host().delay_us(us); }

void sim_delay_us(uint32_t us) { host().delay_us(us); }

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
    if (!sim::valid_pin(pin)) {
        sim::log_warning("attachInterrupt(): no such GPIO; the call is ignored.");
        return;
    }
    sim::g_isr[pin] = sim::Isr{nullptr, handler, arg};
    host().attach_interrupt(pin, sim_mode);
}

void attachInterrupt(uint8_t pin, void (*userFunc)(void), int mode)
{
    attachInterruptArg(pin, nullptr, nullptr, mode);
    if (sim::valid_pin(pin)) {
        sim::g_isr[pin] = sim::Isr{userFunc, nullptr, nullptr};
    }
}

void detachInterrupt(uint8_t pin)
{
    if (sim::valid_pin(pin)) {
        sim::g_isr[pin] = sim::Isr{nullptr, nullptr, nullptr};
        host().detach_interrupt(pin);
    }
}

void sim_interrupts_enable(void)
{
    host().set_interrupts_enabled(1);
}

void sim_interrupts_disable(void)
{
    host().set_interrupts_enabled(0);
}

uint32_t getCpuFrequencyMhz(void) { return 240; }

bool setCpuFrequencyMhz(uint32_t mhz)
{
    (void)mhz;
    static bool warned = false;
    sim::warn_once(warned, "setCpuFrequencyMhz(): the CPU clock is not simulated; call costs stay those of 240 MHz.");
    return true;
}

uint32_t getApbFrequency(void) { return 80000000u; }

uint32_t getXtalFrequencyMhz(void) { return 40; }

// esp_random() is a hardware RNG on the chip; here a xorshift generator, so runs repeat exactly.
uint32_t esp_random(void)
{
    uint32_t x = sim::g_random_state;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    sim::g_random_state = x;
    return x;
}

void esp_fill_random(void* buffer, size_t length)
{
    uint8_t* bytes = static_cast<uint8_t*>(buffer);
    for (size_t i = 0; i < length; i++) {
        bytes[i] = static_cast<uint8_t>(esp_random());
    }
}

long random(void) { return static_cast<long>(esp_random() & 0x7FFFFFFF); }

void srandom(unsigned long seed) { sim::g_random_state = seed != 0 ? static_cast<uint32_t>(seed) : 0x9E3779B9u; }

}  // extern "C"

void tone(uint8_t pin, unsigned int frequency, unsigned long duration) { host().tone(pin, frequency, static_cast<uint32_t>(duration)); }

void noTone(uint8_t pin) { host().no_tone(pin); }

long random(long howbig)
{
    if (howbig <= 0) {
        return 0;
    }
    return static_cast<long>(esp_random() % static_cast<uint32_t>(howbig));
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
