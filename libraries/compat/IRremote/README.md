# IRremote compat

`src/private/IRTimer.hpp` replaces the vendored file of the same path (IRremote 4.7.1) at build
time. The original programs a hardware timer for each CPU it knows, and stops with
"no timer functions implemented for this CPU" elsewhere. The replacement takes the 50 µs receive
tick from a simulated timer interrupt (ABI 2.2, `sim/timers.h`), the same on every board, and
calls the library's `IRReceiveTimerInterruptHandler()` from it. Sending with `SEND_PWM_BY_TIMER`
(the default on ESP32) is not simulated: there is no IR LED part, so the send pin goes to OUTPUT
and stays low, with a warning. Re-check when IRremote is updated: the functions this file defines
are the ones the vendored IRTimer.hpp declares at its top.