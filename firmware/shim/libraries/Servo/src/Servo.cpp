// Servo pulses from one simulated timer interrupt (see Servo.h).
#include "Servo.h"

#include <sim/host.h>
#include <sim/timers.h>

namespace {

struct Channel {
    int8_t pin;
    bool active;
    uint16_t micros;
};

Channel g_channels[MAX_SERVOS];
uint8_t g_count = 0;
int g_timer = -1;
int8_t g_current = -1;
uint32_t g_frameUsed = 0;
uint32_t g_frame = REFRESH_INTERVAL;

// The timer interrupt: end the current pulse, start the next one, or pad the frame to 20 ms.
void tick(void*)
{
    if (g_current >= 0 && g_channels[g_current].active) {
        digitalWrite(static_cast<uint8_t>(g_channels[g_current].pin), LOW);
    }
    for (g_current++; g_current < g_count; g_current++) {
        Channel& channel = g_channels[g_current];
        if (channel.active) {
            // The timer first: the pulse then ends channel.micros after the timer restarted, which
            // leaves out the time digitalWrite takes (4 us on a Nano), as the AVR library trims it.
            g_frameUsed += channel.micros;
            sim_timer_start(g_timer, static_cast<uint64_t>(channel.micros) * 1000u, tick, nullptr);
            digitalWrite(static_cast<uint8_t>(channel.pin), HIGH);
            return;
        }
    }
    uint32_t rest = g_frameUsed + 100 < g_frame ? g_frame - g_frameUsed : 100;
    g_current = -1;
    g_frameUsed = 0;
    sim_timer_start(g_timer, static_cast<uint64_t>(rest) * 1000u, tick, nullptr);
}

bool anyActive()
{
    for (uint8_t i = 0; i < g_count; i++) {
        if (g_channels[i].active) {
            return true;
        }
    }
    return false;
}

}  // namespace

Servo::Servo() : min(0), max(0)
{
    if (g_count < MAX_SERVOS) {
        servoIndex = g_count++;
        g_channels[servoIndex] = Channel{-1, false, DEFAULT_PULSE_WIDTH};
    } else {
        servoIndex = INVALID_SERVO;
    }
}

uint8_t Servo::attach(int pin) { return attach(pin, MIN_PULSE_WIDTH, MAX_PULSE_WIDTH); }

uint8_t Servo::attach(int pin, int minimum, int maximum)
{
    if (servoIndex == INVALID_SERVO) {
        sim::log_warning("Servo: more than 12 servos; this one is not attached.");
        return INVALID_SERVO;
    }
    pinMode(static_cast<uint8_t>(pin), OUTPUT);
    digitalWrite(static_cast<uint8_t>(pin), LOW);
    min = static_cast<int16_t>(minimum);
    max = static_cast<int16_t>(maximum);
    Channel& channel = g_channels[servoIndex];
    channel.pin = static_cast<int8_t>(pin);
    channel.active = true;
    if (g_timer < 0) {
        g_timer = sim_timer_claim();
        if (g_timer < 0) {
            channel.active = false;
            return INVALID_SERVO;
        }
        g_current = -1;
        g_frameUsed = 0;
        sim_timer_start(g_timer, 1000u * 1000u, tick, nullptr);  // the first frame starts 1 ms from now
    }
    return servoIndex;
}

void Servo::detach()
{
    if (servoIndex == INVALID_SERVO) {
        return;
    }
    g_channels[servoIndex].active = false;
    if (!anyActive() && g_timer >= 0) {
        sim_timer_release(g_timer);
        g_timer = -1;
    }
}

void Servo::write(int value)
{
    if (value < MIN_PULSE_WIDTH) {
        value = constrain(value, 0, 180);
        value = static_cast<int>(map(value, 0, 180, min, max));
    }
    writeMicroseconds(value);
}

void Servo::writeMicroseconds(int value)
{
    if (servoIndex == INVALID_SERVO) {
        return;
    }
    g_channels[servoIndex].micros = static_cast<uint16_t>(constrain(value, static_cast<int>(min), static_cast<int>(max)));
}

int Servo::read() { return static_cast<int>(map(readMicroseconds() + 1, min, max, 0, 180)); }

int Servo::readMicroseconds() { return servoIndex == INVALID_SERVO ? 0 : g_channels[servoIndex].micros; }

bool Servo::attached() { return servoIndex != INVALID_SERVO && g_channels[servoIndex].active; }

void Servo::setPeriodHertz(int hertz)
{
    if (hertz > 0) {
        g_frame = 1000000u / static_cast<uint32_t>(hertz);
    }
}