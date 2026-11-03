// Serial: forwards to the simulator's UART model (TX ring, baud-rate drain, RX buffer).
#include "HardwareSerial.h"

#include "sim/host.h"

using sim::host;

HardwareSerial Serial(0);

void HardwareSerial::begin(unsigned long baud, uint8_t config) { host().serial_begin(port_, static_cast<uint32_t>(baud), config, SIM_PIN_DEFAULT, SIM_PIN_DEFAULT); }

void HardwareSerial::end() { host().serial_end(port_); }

int HardwareSerial::available(void) { return host().serial_available(port_); }

int HardwareSerial::peek(void) { return host().serial_peek(port_); }

int HardwareSerial::read(void) { return host().serial_read(port_); }

int HardwareSerial::availableForWrite(void) { return host().serial_available_for_write(port_); }

void HardwareSerial::flush(void) { host().serial_flush(port_); }

size_t HardwareSerial::write(uint8_t c)
{
    host().serial_write(port_, &c, 1);
    return 1;
}

size_t HardwareSerial::write(const uint8_t* buffer, size_t size)
{
    return static_cast<size_t>(host().serial_write(port_, buffer, static_cast<int32_t>(size)));
}
