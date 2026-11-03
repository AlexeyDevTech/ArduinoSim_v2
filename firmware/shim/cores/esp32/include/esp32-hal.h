/*
 * esp32-hal.h - the Arduino-ESP32 hardware layer as sketches use it: ADC, LEDC PWM, DAC, tone,
 * interrupts with an argument, CPU frequency, timers and the ESP-IDF calls sketches make directly.
 * Peripherals the simulator does not model are declared unavailable, so a sketch that uses them
 * fails to compile with a message naming them (docs/adr/ADR-009-esp-boards.md).
 */
#ifndef ESP32_HAL_H
#define ESP32_HAL_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define SIM_ESP_UNAVAILABLE(what) __attribute__((unavailable("ArduSim does not simulate " what)))

#ifdef __cplusplus
extern "C" {
#endif

/* ---- ADC ---- */
typedef enum { ADC_0db, ADC_2_5db, ADC_6db, ADC_11db, ADC_ATTENDB_MAX } adc_attenuation_t;

void analogReadResolution(uint8_t bits);
void analogSetWidth(uint8_t bits);
uint32_t analogReadMilliVolts(uint8_t pin);
void analogSetAttenuation(adc_attenuation_t attenuation);
void analogSetPinAttenuation(uint8_t pin, adc_attenuation_t attenuation);
int8_t digitalPinToAnalogChannel(uint8_t pin);
int8_t analogChannelToDigitalPin(uint8_t channel);

/* ---- PWM (LEDC), core 3.x API with the 2.x channel functions ---- */
typedef enum { NOTE_C, NOTE_Cs, NOTE_D, NOTE_Eb, NOTE_E, NOTE_F, NOTE_Fs, NOTE_G, NOTE_Gs, NOTE_A, NOTE_Bb, NOTE_B, NOTE_MAX } note_t;

bool ledcAttach(uint8_t pin, uint32_t freq, uint8_t resolution);
bool ledcAttachChannel(uint8_t pin, uint32_t freq, uint8_t resolution, uint8_t channel);
bool ledcWrite(uint8_t pinOrChannel, uint32_t duty);
uint32_t ledcWriteTone(uint8_t pin, uint32_t freq);
uint32_t ledcWriteNote(uint8_t pin, note_t note, uint8_t octave);
uint32_t ledcRead(uint8_t pin);
uint32_t ledcReadFreq(uint8_t pin);
bool ledcDetach(uint8_t pin);
uint32_t ledcChangeFrequency(uint8_t pin, uint32_t freq, uint8_t resolution);
uint32_t ledcSetup(uint8_t channel, uint32_t freq, uint8_t resolution);
void ledcAttachPin(uint8_t pin, uint8_t channel);
void ledcDetachPin(uint8_t pin);
bool ledcFade(uint8_t pin, uint32_t start_duty, uint32_t target_duty, int max_fade_time_ms) SIM_ESP_UNAVAILABLE("LEDC hardware fading");

void analogWriteResolution(uint8_t pin, uint8_t resolution);
void analogWriteFrequency(uint8_t pin, uint32_t freq);

/* ---- DAC (GPIO 25 and 26) ---- */
void dacWrite(uint8_t pin, uint8_t value);
void dacDisable(uint8_t pin);

/* ---- Interrupts with an argument ---- */
void attachInterruptArg(uint8_t pin, void (*handler)(void*), void* arg, int mode);

/* ---- CPU ---- */
uint32_t getCpuFrequencyMhz(void);
bool setCpuFrequencyMhz(uint32_t mhz);
uint32_t getApbFrequency(void);
uint32_t getXtalFrequencyMhz(void);

/* ---- ESP-IDF calls sketches use directly ---- */
int64_t esp_timer_get_time(void);
uint32_t esp_random(void);
void esp_fill_random(void* buffer, size_t length);
void esp_restart(void) __attribute__((noreturn));
uint32_t esp_get_free_heap_size(void);
uint32_t esp_get_minimum_free_heap_size(void);
void delayMicroseconds(unsigned int us);
unsigned long micros(void);

/* ---- Reset reason and deep sleep (timer wake-up only) ---- */
typedef enum {
    ESP_RST_UNKNOWN,
    ESP_RST_POWERON,
    ESP_RST_EXT,
    ESP_RST_SW,
    ESP_RST_PANIC,
    ESP_RST_INT_WDT,
    ESP_RST_TASK_WDT,
    ESP_RST_WDT,
    ESP_RST_DEEPSLEEP,
    ESP_RST_BROWNOUT,
    ESP_RST_SDIO
} esp_reset_reason_t;

typedef enum {
    ESP_SLEEP_WAKEUP_UNDEFINED,
    ESP_SLEEP_WAKEUP_ALL,
    ESP_SLEEP_WAKEUP_EXT0,
    ESP_SLEEP_WAKEUP_EXT1,
    ESP_SLEEP_WAKEUP_TIMER,
    ESP_SLEEP_WAKEUP_TOUCHPAD,
    ESP_SLEEP_WAKEUP_ULP
} esp_sleep_wakeup_cause_t;

esp_reset_reason_t esp_reset_reason(void);
esp_sleep_wakeup_cause_t esp_sleep_get_wakeup_cause(void);
int esp_sleep_enable_timer_wakeup(uint64_t time_in_us);
void esp_deep_sleep_start(void) __attribute__((noreturn));
void esp_deep_sleep(uint64_t time_in_us) __attribute__((noreturn));
int esp_sleep_enable_ext0_wakeup(int gpio_num, int level) SIM_ESP_UNAVAILABLE("deep-sleep wake-up from a pin (use the timer)");
int esp_sleep_enable_touchpad_wakeup(void) SIM_ESP_UNAVAILABLE("deep-sleep wake-up from touch (use the timer)");

/* ---- Not simulated: fail at compile time ---- */
typedef struct hw_timer_s hw_timer_t;
hw_timer_t* timerBegin(uint32_t frequency) SIM_ESP_UNAVAILABLE("ESP32 hardware timers (timerBegin); use millis() or a FreeRTOS task");
uint16_t touchRead(uint8_t pin) SIM_ESP_UNAVAILABLE("ESP32 touch sensing (touchRead)");
float temperatureRead(void) SIM_ESP_UNAVAILABLE("the ESP32 internal temperature sensor");
int hallRead(void) SIM_ESP_UNAVAILABLE("the ESP32 hall sensor");

#ifdef __cplusplus
}
#endif

#endif /* ESP32_HAL_H */
