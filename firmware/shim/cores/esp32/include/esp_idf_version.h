/* esp_idf_version.h - the ESP-IDF version Arduino-ESP32 3.0 builds on; libraries pick their code paths by it.
 * The simulator does not build ESP-IDF: its drivers are not there (docs/adr/ADR-009-esp-boards.md). */
#ifndef ESP_IDF_VERSION_H
#define ESP_IDF_VERSION_H

#define ESP_IDF_VERSION_MAJOR 5
#define ESP_IDF_VERSION_MINOR 1
#define ESP_IDF_VERSION_PATCH 4
#define ESP_IDF_VERSION_VAL(major, minor, patch) ((major << 16) | (minor << 8) | (patch))
#define ESP_IDF_VERSION ESP_IDF_VERSION_VAL(ESP_IDF_VERSION_MAJOR, ESP_IDF_VERSION_MINOR, ESP_IDF_VERSION_PATCH)

#endif /* ESP_IDF_VERSION_H */
