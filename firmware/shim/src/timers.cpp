// Periodic timer interrupts over the host's timer_start/timer_stop (sim/timers.h).
#include "sim/timers.h"

#include <stdio.h>

#include "sim/host.h"

using sim::host;

namespace {

struct Timer {
    bool claimed;
    void (*handler)(void*);
    void* arg;
};

Timer g_timers[SIM_TIMER_COUNT];

bool valid(int timer) { return timer >= 0 && timer < SIM_TIMER_COUNT; }

}  // namespace

namespace sim {

bool dispatch_timer(int32_t id)
{
    int timer = id - SIM_TIMER_INTERRUPT_BASE;
    if (!valid(timer)) {
        return false;
    }
    if (g_timers[timer].handler != nullptr) {
        g_timers[timer].handler(g_timers[timer].arg);
    }
    return true;
}

}  // namespace sim

extern "C" {

int sim_timer_claim(void)
{
    for (int timer = 0; timer < SIM_TIMER_COUNT; timer++) {
        if (!g_timers[timer].claimed) {
            g_timers[timer].claimed = true;
            return timer;
        }
    }
    sim::log_warning("Timer interrupts: all four simulated timers are in use; one more was asked for.");
    return -1;
}

void sim_timer_release(int timer)
{
    if (valid(timer)) {
        sim_timer_stop(timer);
        g_timers[timer].claimed = false;
    }
}

void sim_timer_start(int timer, uint64_t period_ns, void (*handler)(void*), void* arg)
{
    if (!valid(timer)) {
        char message[80];
        snprintf(message, sizeof message, "Timer interrupts: there is no timer %d; the call is ignored.", timer);
        sim::log_warning(message);
        return;
    }
    g_timers[timer].handler = handler;
    g_timers[timer].arg = arg;
    host().timer_start(timer, period_ns);
}

void sim_timer_stop(int timer)
{
    if (valid(timer)) {
        host().timer_stop(timer);
        g_timers[timer].handler = nullptr;
        g_timers[timer].arg = nullptr;
    }
}

}  // extern "C"
