/*
 * ESP32Servo.h - the ESP32Servo library's API for ArduSim: its Servo class is the shared Servo
 * library (pulses from a simulated timer interrupt); the LEDC timers it allocates on a real ESP32
 * are not needed, so ESP32PWM::allocateTimer() does nothing.
 */
#ifndef ESP32Servo_h
#define ESP32Servo_h

#include <Servo.h>

class ESP32PWM {
public:
    static void allocateTimer(int) {}
};

#endif