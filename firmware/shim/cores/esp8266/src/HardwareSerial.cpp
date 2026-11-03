// Serial and Serial1 of the ESP8266 core over the simulator's UART model (see HardwareSerial.h).
#include "HardwareSerial.h"

#include "sim/host.h"

using sim::host;

HardwareSerial Serial(0);
HardwareSerial Serial1(1);

namespace {

// The core's config byte (data bits in bits 2-3, parity in 0-1, stop bits in 4-5) as the AVR
// UCSRC value the host's UART model reads.
uint32_t host_config(uint32_t config)
{
    uint32_t data = (config >> 2) & 3u;
    uint32_t parity = config & 3u;
    bool two_stop = ((config >> 4) & 3u) == 3u;
    return (data << 1) | (two_stop ? 0x08u : 0u) | (parity == 2u ? 0x20u : parity == 3u ? 0x30u : 0u);
}

}  // namespace

void HardwareSerial::begin(unsigned long baud, SerialConfig config, SerialMode, uint8_t, bool)
{
    baud_ = baud;
    host().serial_begin(port_, static_cast<uint32_t>(baud), host_config(config), SIM_PIN_DEFAULT, SIM_PIN_DEFAULT);
}

void HardwareSerial::end() { host().serial_end(port_); }

void HardwareSerial::updateBaudRate(unsigned long baud)
{
    baud_ = baud;
    host().serial_begin(port_, static_cast<uint32_t>(baud), host_config(SERIAL_8N1), SIM_PIN_DEFAULT, SIM_PIN_DEFAULT);
}

void HardwareSerial::swap(uint8_t)
{
    static bool warned = false;
    if (!warned) {
        warned = true;
        sim::log_warning("Serial.swap(): moving UART0 to GPIO 13/15 is not simulated; the Serial Monitor keeps showing Serial.");
    }
}

int HardwareSerial::available(void) { return port_ == 0 ? host().serial_available(port_) : 0; }

int HardwareSerial::peek(void) { return port_ == 0 ? host().serial_peek(port_) : -1; }

int HardwareSerial::read(void) { return port_ == 0 ? host().serial_read(port_) : -1; }

size_t HardwareSerial::read(char* buffer, size_t size)
{
    size_t count = 0;
    while (count < size) {
        int value = read();
        if (value < 0) {
            break;
        }
        buffer[count++] = static_cast<char>(value);
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