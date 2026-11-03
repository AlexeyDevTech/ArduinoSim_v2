#ifndef OneWire_Direct_GPIO_h
#define OneWire_Direct_GPIO_h

// ArduSim replacement for OneWire 2.3.8's util/OneWire_direct_gpio.h (libraries/compat): the
// simulator has no GPIO registers to reach directly, so every board uses the Arduino calls. This
// is the vendored file's own fallback branch, without its "not guaranteed" warning, and for the
// ESP boards too, whose branches use the real chips' register blocks.

#include <stdint.h>

#define PIN_TO_BASEREG(pin)             (0)
#define PIN_TO_BITMASK(pin)             (pin)
#define IO_REG_TYPE unsigned int
#define IO_REG_BASE_ATTR
#define IO_REG_MASK_ATTR
#define DIRECT_READ(base, pin)          digitalRead(pin)
#define DIRECT_WRITE_LOW(base, pin)     digitalWrite(pin, LOW)
#define DIRECT_WRITE_HIGH(base, pin)    digitalWrite(pin, HIGH)
#define DIRECT_MODE_INPUT(base, pin)    pinMode(pin,INPUT)
#define DIRECT_MODE_OUTPUT(base, pin)   pinMode(pin,OUTPUT)

#endif