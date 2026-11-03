// Serial, Serial1, Serial2 of the ESP32 core over the simulator's UART model (see HardwareSerial.h).
#include "HardwareSerial.h"

#include "sim/host.h"

using sim::host;

HardwareSerial Serial(0);
HardwareSerial Serial1(1);
HardwareSerial Serial2(2);

namespace {

// The core's config word (data bits in bits 2-3, parity in 0-1, stop bits in 4-5) as the
// AVR UCSRC value the host's UART model reads (data bits in 1-2, two stop bits in 3, parity in 4-5).
uint32_t host_config(uint32_t config)
{
    uint32_t data = (config >> 2) & 3u;
    uint32_t parity = config & 3u;
    bool two_stop = ((config >> 4) & 3u) == 3u;
    return (data << 1) | (two_stop ? 0x08u : 0u) | (parity == 2u ? 0x20u : parity == 3u ? 0x30u : 0u);
}

}  // namespace

void HardwareSerial::begin(unsigned long baud, uint32_t config, int8_t rxPin, int8_t txPin, bool, unsigned long, uint8_t)
{
    baud_ = static_cast<uint32_t>(baud);
    begun_ = true;
    host().serial_begin(port_, baud_, host_config(config), rxPin, txPin);
}

void HardwareSerial::end()
{
    begun_ = false;
    host().serial_end(port_);
}

void HardwareSerial::updateBaudRate(unsigned long baud)
{
    baud_ = static_cast<uint32_t>(baud);
    host().serial_begin(port_, baud_, host_config(SERIAL_8N1), SIM_PIN_DEFAULT, SIM_PIN_DEFAULT);
}

bool HardwareSerial::setPins(int8_t rxPin, int8_t txPin, int8_t, int8_t)
{
    if (begun_) {
        host().serial_begin(port_, baud_, host_config(SERIAL_8N1), rxPin, txPin);
    }
    return true;
}

int HardwareSerial::available(void) { return host().serial_available(port_); }

int HardwareSerial::peek(void) { return host().serial_peek(port_); }

int HardwareSerial::read(void) { return host().serial_read(port_); }

size_t HardwareSerial::read(uint8_t* buffer, size_t size)
{
    size_t count = 0;
    while (count < size) {
        int value = read();
        if (value < 0) {
            break;
        }
        buffer[count++] = static_cast<uint8_t>(value);
    }
    return count;
}

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
