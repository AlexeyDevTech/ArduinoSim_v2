/*
 * sim/network_wait.h - what a network call does while there is nothing to read or accept yet:
 * on the ESP32 the other due FreeRTOS tasks get their turn, on the ESP8266 the system does
 * (yield), so a server and a client in one sketch do not wait for each other.
 */
#ifndef SIM_NETWORK_WAIT_H
#define SIM_NETWORK_WAIT_H

#include <Arduino.h>

namespace sim {

inline void network_wait()
{
#if defined(ARDUINO_ARCH_ESP32)
    sim_task_switch_point();
#else
    yield();
#endif
}

}  // namespace sim

#endif