/*
 * esp32-hal-log.h - the core's log_x() macros. As with the default "Core Debug Level: None",
 * only errors are printed; they go to Serial with the core's "[E]" prefix.
 */
#ifndef ESP32_HAL_LOG_H
#define ESP32_HAL_LOG_H

#include <stdio.h>

#ifdef __cplusplus
extern "C" {
#endif
void sim_log_printf(const char* format, ...) __attribute__((format(printf, 1, 2)));
#ifdef __cplusplus
}
#endif

#define ARDUHAL_LOG_LEVEL_NONE 0
#define ARDUHAL_LOG_LEVEL_ERROR 1
#define ARDUHAL_LOG_LEVEL_WARN 2
#define ARDUHAL_LOG_LEVEL_INFO 3
#define ARDUHAL_LOG_LEVEL_DEBUG 4
#define ARDUHAL_LOG_LEVEL_VERBOSE 5

#define log_v(format, ...) ((void)0)
#define log_d(format, ...) ((void)0)
#define log_i(format, ...) ((void)0)
#define log_w(format, ...) ((void)0)
#define log_e(format, ...) sim_log_printf("[E] " format "\r\n", ##__VA_ARGS__)
#define log_n(format, ...) sim_log_printf("[E] " format "\r\n", ##__VA_ARGS__)
#define isr_log_e(format, ...) log_e(format, ##__VA_ARGS__)
#define ESP_LOGE(tag, format, ...) sim_log_printf("E (%s) " format "\r\n", tag, ##__VA_ARGS__)
#define ESP_LOGW(tag, format, ...) ((void)0)
#define ESP_LOGI(tag, format, ...) ((void)0)
#define ESP_LOGD(tag, format, ...) ((void)0)
#define ESP_LOGV(tag, format, ...) ((void)0)

#endif /* ESP32_HAL_LOG_H */
