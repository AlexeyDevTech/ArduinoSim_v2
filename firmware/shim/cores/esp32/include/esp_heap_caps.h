/* esp_heap_caps.h - ESP-IDF capability-based allocation. The simulated ESP32 has one heap (no PSRAM,
 * no DMA or IRAM regions), so every capability allocates from it. */
#ifndef ESP_HEAP_CAPS_H
#define ESP_HEAP_CAPS_H

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>

#define MALLOC_CAP_EXEC (1 << 0)
#define MALLOC_CAP_32BIT (1 << 1)
#define MALLOC_CAP_8BIT (1 << 2)
#define MALLOC_CAP_DMA (1 << 3)
#define MALLOC_CAP_SPIRAM (1 << 10)
#define MALLOC_CAP_INTERNAL (1 << 11)
#define MALLOC_CAP_DEFAULT (1 << 12)

#ifdef __cplusplus
extern "C" {
#endif

static inline void* heap_caps_malloc(size_t size, uint32_t caps) { (void)caps; return malloc(size); }
static inline void* heap_caps_calloc(size_t n, size_t size, uint32_t caps) { (void)caps; return calloc(n, size); }
static inline void* heap_caps_realloc(void* ptr, size_t size, uint32_t caps) { (void)caps; return realloc(ptr, size); }
static inline void heap_caps_free(void* ptr) { free(ptr); }

#ifdef __cplusplus
}
#endif

#endif /* ESP_HEAP_CAPS_H */