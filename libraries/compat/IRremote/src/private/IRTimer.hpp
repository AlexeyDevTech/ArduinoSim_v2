/*
 * IRTimer.hpp - ArduSim replacement for IRremote 4.7.1's src/private/IRTimer.hpp (libraries/compat).
 *
 * The vendored file programs a hardware timer per CPU. In the simulator the 50 us receive tick
 * comes from a simulated timer interrupt (ABI 2.2, sim/timers.h) on every board, so this file
 * defines the same functions for ARDUINO_ARCH_SIM only. Sending by timer PWM is not simulated:
 * there is no IR LED part, and the send pin only goes to OUTPUT, with a warning.
 */
#ifndef _IR_TIMER_HPP
#define _IR_TIMER_HPP

#include <sim/host.h>
#include <sim/timers.h>

void timerConfigForReceive();
void timerEnableReceiveInterrupt();
void timerDisableReceiveInterrupt();
void timerResetInterruptPending();

void timerConfigForSend(uint16_t aFrequencyKHz);
void enableSendPWMByTimer();
void disableSendPWMByTimer();

void IRReceiveTimerInterruptHandler(); // defined in IRReceive.hpp

#if defined(SEND_PWM_BY_TIMER) && (defined(ESP32) || defined(ARDUINO_ARCH_RP2040) || defined(PARTICLE) || defined(ARDUINO_ARCH_MBED))
#define SEND_PWM_DOES_NOT_USE_RECEIVE_TIMER
#endif

#if defined(SEND_PWM_BY_TIMER) && !defined(SEND_PWM_DOES_NOT_USE_RECEIVE_TIMER)
// IRremote.hpp set a placeholder; on a Nano the library sends on Timer2's OC2B pin.
#  undef IR_SEND_PIN
#define IR_SEND_PIN 3
#endif

// The receive handler is called as a plain function, not through an interrupt vector.
#if defined(ISR)
#undef ISR
#endif

static int sIRremoteSimTimer = -1;

static void IRremoteSimTimerTick(void*) { IRReceiveTimerInterruptHandler(); }

void timerConfigForReceive() {
    if (sIRremoteSimTimer < 0) {
        sIRremoteSimTimer = sim_timer_claim();
    }
}

void timerEnableReceiveInterrupt() {
    if (sIRremoteSimTimer >= 0) {
        sim_timer_start(sIRremoteSimTimer, MICROS_PER_TICK * 1000ULL, IRremoteSimTimerTick, nullptr);
    }
}

void timerDisableReceiveInterrupt() {
    if (sIRremoteSimTimer >= 0) {
        sim_timer_stop(sIRremoteSimTimer);
    }
}

void timerResetInterruptPending() {
}

#if defined(SEND_PWM_BY_TIMER)
void timerConfigForSend(uint16_t aFrequencyKHz) {
    (void) aFrequencyKHz;
#  if !defined(SEND_PWM_DOES_NOT_USE_RECEIVE_TIMER)
    timerDisableReceiveInterrupt();
#  endif
    static bool sWarned = false;
    if (!sWarned) {
        sWarned = true;
        sim::log_warning("IRremote: sending IR by timer PWM is not simulated; the send pin stays low.");
    }
#  if defined(IR_SEND_PIN)
    pinMode(IR_SEND_PIN, OUTPUT);
#  else
    pinMode(IrSender.sendPin, OUTPUT);
#  endif
}

void enableSendPWMByTimer() {
}

void disableSendPWMByTimer() {
}
#endif // defined(SEND_PWM_BY_TIMER)

#endif // _IR_TIMER_HPP
