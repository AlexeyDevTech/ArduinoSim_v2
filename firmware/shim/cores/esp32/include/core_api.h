/*
 * core_api.h - API of the Arduino-ESP32 core beyond the shared Arduino.h, included at its end:
 * the hardware layer, FreeRTOS (which the real Arduino.h includes too), the ESP object.
 */
#ifndef Core_api_h
#define Core_api_h

#include "esp32-hal.h"
#include "esp32-hal-log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include "freertos/task.h"

#ifdef __cplusplus
#include "Esp.h"

void attachInterrupt(uint8_t pin, void (*userFunc)(void), int mode);
#endif

#define digitalPinIsValid(pin) ((pin) < 40 && digitalPinToGPIONumber(pin) >= 0)
#define digitalPinCanOutput(pin) ((pin) < 34)
#define digitalPinToGPIONumber(pin) ((int8_t)(pin))
#define digitalPinToTouchChannel(pin) (-1)

#endif /* Core_api_h */
