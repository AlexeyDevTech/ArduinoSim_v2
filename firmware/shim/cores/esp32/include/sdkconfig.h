/* sdkconfig.h - ESP-IDF's build configuration. The simulator does not build ESP-IDF, so no target or
 * driver option (CONFIG_IDF_TARGET_*, CONFIG_*_ENABLED) is set and code that needs ESP-IDF drivers
 * stays out of the build. */
#ifndef SIM_SDKCONFIG_H
#define SIM_SDKCONFIG_H

#define CONFIG_FREERTOS_HZ 1000

#endif /* SIM_SDKCONFIG_H */
