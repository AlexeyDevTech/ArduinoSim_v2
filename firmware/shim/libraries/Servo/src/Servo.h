/*
 * Servo.h - the Arduino Servo library's API for ArduSim (docs/adr/ADR-012-outputs.md).
 *
 * As on AVR, one timer interrupt walks the attached servos in turn: a pin goes HIGH, the timer
 * fires after that servo's pulse width and takes it LOW, and so on; the frame is padded to 20 ms.
 * The timer is the simulator's (sim/timers.h), so the pulses have the same timing on every board.
 */
#ifndef Servo_h
#define Servo_h

#include <Arduino.h>

#define MIN_PULSE_WIDTH 544      // the shortest pulse sent to a servo
#define MAX_PULSE_WIDTH 2400     // the longest pulse sent to a servo
#define DEFAULT_PULSE_WIDTH 1500 // the pulse after attach()
#define REFRESH_INTERVAL 20000   // minimum time to refresh servos, in microseconds
#define SERVOS_PER_TIMER 12
#define MAX_SERVOS 12
#define INVALID_SERVO 255

class Servo {
public:
    Servo();
    uint8_t attach(int pin);                  // attach the pin; returns the channel, or INVALID_SERVO
    uint8_t attach(int pin, int min, int max); // with the pulse widths for 0 and 180 degrees, in microseconds
    void detach();
    void write(int value);                    // below MIN_PULSE_WIDTH: an angle; otherwise microseconds
    void writeMicroseconds(int value);
    int read();                               // the last angle written, 0-180
    int readMicroseconds();
    bool attached();
    void setPeriodHertz(int hertz);           // ESP32Servo: the frame rate (default 50 Hz, all servos)

protected:
    uint8_t servoIndex;
    int16_t min;
    int16_t max;
};

#endif