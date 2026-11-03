# ADR-009: ESP32 and ESP8266 boards

- Status: Accepted (the owner's answers of 2026-11-02 to the M6+ questions).
- Date: 2026-11-02

## Context

Brief §4 "Later" item 1 adds the ESP32 DevKit v1, NodeMCU v3 and Wemos D1 mini. They differ from the Nano in ways the ABI v1 cannot express: two I2C buses and two usable SPI buses on any pins, UARTs on chosen pins, PWM with a chosen frequency and resolution, a DAC, a software restart, a key-value store (`Preferences`) and FreeRTOS tasks. The approach of ADR-001 stays: the sketch is compiled for the host against our shim; no CPU is emulated.

## Decision

### Firmware ABI 2.0

A major version, not appended functions: firmware.dll is always built by the same app that hosts it, so there is no old firmware to keep working, and the object cache is keyed by the shim version anyway.

- Bus functions take the bus first: `i2c_begin(bus, sda, scl)`, `i2c_start(bus, …)`, … and `spi_begin(bus, sck, miso, mosi, ss)`, `spi_transfer(bus, value)`, …; a pin of -1 means the board's default. The Nano has bus 0 only.
- `serial_begin(port, baud, config, rx_pin, tx_pin)`.
- `pin_mode` accepts `INPUT_PULLDOWN` and `OUTPUT_OPEN_DRAIN` where the board has them (the board profile says which pins can).
- `pwm_config(pin, frequency_hz, resolution_bits)`; `analog_write(pin, duty)` takes the duty at that resolution. The Nano keeps its fixed 490/980 Hz, 8 bits.
- `dac_write(pin, value)`, `restart()` (never returns), the key-value store `kv_*` and the Wi-Fi and network functions of ADR-010.
- The Runner rejects firmware with another major version, as today.

### Predefined macros

As the real cores set them: `ESP32`, `ARDUINO_ARCH_ESP32`, `ARDUINO_ESP32_DEV`, `F_CPU=240000000L` (ESP8266: `ESP8266`, `ARDUINO_ARCH_ESP8266`, `ARDUINO_ESP8266_NODEMCU_ESP12E` or `ARDUINO_ESP8266_WEMOS_D1MINI`, `F_CPU=80000000L`), plus `ARDUINO_ARCH_SIM` and `SIM_BOARD_<id>`. Sketches pick `WiFi.h` or `ESP8266WiFi.h` with them. Library code behind these macros that calls ESP-IDF or SDK functions gets a compat shim in `libraries/compat/` (or a minimal IDF header in the shim), never a silent patch.

### API level

The Arduino-ESP32 core 3.x API, plus the 2.x `ledcSetup`/`ledcAttachPin`/`ledcWrite(channel, …)` that most tutorials still use. ESP8266: the ESP8266 Arduino core 3.x API.

### FreeRTOS: cooperative tasks

`xTaskCreate`, `xTaskCreatePinnedToCore`, `vTaskDelay`, `vTaskDelayUntil`, `vTaskDelete`, `taskYIELD`, `xTaskGetTickCount`, queues, semaphores and mutexes run as cooperative tasks on Windows fibers inside the firmware thread; `loop()` runs in the `loopTask` as on the real core. A task gives up the CPU in `vTaskDelay`, `delay`, `yield`, and while it waits for a queue, semaphore or notification; the shim then switches to the ready task that is due first, and asks the host to advance time only when none is ready. Runs stay deterministic. Limits, shown in the accuracy sheet: no preemption (a task that never waits starves the others; the busy watchdog still catches a task that makes no API calls), the core number is accepted and ignored, priorities only order tasks that are due at the same time. Other FreeRTOS functions fail to compile with a message naming them.

### Storage

- ESP `EEPROM.begin(size)` / `commit()` emulate flash: `commit()` writes the changed bytes to `eeprom.bin` (size from the board profile, 4096 for ESP32 and ESP8266).
- `Preferences` goes through the host's `kv_*` calls and persists in the project folder as `preferences.json`: `{ "<namespace>": { "<key>": { "type": "u32", "value": 42 } } }`, types as the Preferences API names them (`i8`…`u64`, `float`, `double`, `bool`, `string`, `bytes` as base64). Namespace and key names are limited to 15 characters as in NVS; longer ones fail with a warning, as on the board.
- Both files are part of the project (ADR-008); "clear" in the board's properties deletes them.

### Boards in the UI

The board picker switches the board of a project; wires to pins the new board lacks are removed in the same undo step. Board art is a `Part.<board id>` template like any part.

## Consequences

- Every bus call site in the shim and the Runner changes once (M6). The Nano behaves exactly as before; the integration tests prove it.
- ESP sketches that rely on preemption (two tasks spinning without waiting) behave differently from hardware; the accuracy sheet says so.
- The project format grows by two optional files and optional settings (ADR-010); schema version stays 1 because old projects load unchanged.
