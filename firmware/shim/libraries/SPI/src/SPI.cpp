/* SPI.cpp - SPIClass over the simulator's SPI master calls (see SPI.h). */
#include "SPI.h"

#include <sim/host.h>

using sim::host;

#if defined(ARDUINO_ARCH_ESP32)
SPIClass SPI(VSPI);

void SPIClass::begin(int8_t sck, int8_t miso, int8_t mosi, int8_t ss)
{
    ss_ = ss;
    host().spi_begin(bus_, sck, miso, mosi, ss);
    if (hwCs_ && ss_ >= 0) {
        pinMode(static_cast<uint8_t>(ss_), OUTPUT);
        digitalWrite(static_cast<uint8_t>(ss_), HIGH);
    }
    apply();
}

void SPIClass::setFrequency(uint32_t freq)
{
    clock_ = freq;
    apply();
}

void SPIClass::setClockDivider(uint32_t clockDiv)
{
    clock_ = 16000000u / (clockDiv == 0 ? 1 : clockDiv);
    apply();
}

uint32_t SPIClass::transfer32(uint32_t data)
{
    uint32_t in = 0;
    for (int shift = 24; shift >= 0; shift -= 8) {
        in = (in << 8) | transfer(static_cast<uint8_t>(data >> shift));
    }
    return in;
}

void SPIClass::transferBytes(const uint8_t* data, uint8_t* out, uint32_t size)
{
    for (uint32_t i = 0; i < size; i++) {
        uint8_t in = transfer(data != nullptr ? data[i] : 0xFF);
        if (out != nullptr) {
            out[i] = in;
        }
    }
}

void SPIClass::transferBits(uint32_t data, uint32_t* out, uint8_t bits)
{
    uint32_t in = 0;
    for (int shift = ((bits + 7) / 8 - 1) * 8; shift >= 0; shift -= 8) {
        in = (in << 8) | transfer(static_cast<uint8_t>(data >> shift));
    }
    if (out != nullptr) {
        *out = in;
    }
}

void SPIClass::writeBytes(const uint8_t* data, uint32_t size) { transferBytes(data, nullptr, size); }

void SPIClass::writePixels(const void* data, uint32_t size) { transferBytes(static_cast<const uint8_t*>(data), nullptr, size); }

void SPIClass::writePattern(const uint8_t* data, uint8_t size, uint32_t repeat)
{
    for (uint32_t r = 0; r < repeat; r++) {
        transferBytes(data, nullptr, size);
    }
}

void SPIClass::transfer(void* buf, uint32_t count) { transferBytes(static_cast<uint8_t*>(buf), static_cast<uint8_t*>(buf), count); }
#else
SPIClass SPI;

void SPIClass::begin()
{
    host().spi_begin(bus_, SIM_PIN_DEFAULT, SIM_PIN_DEFAULT, SIM_PIN_DEFAULT, SIM_PIN_DEFAULT);
    apply();
}

void SPIClass::setClockDivider(uint8_t clockDiv)
{
    static const uint8_t dividers[8] = {4, 16, 64, 128, 2, 8, 32, 64};
    clock_ = F_CPU / dividers[clockDiv & 0x07];
    apply();
}

void SPIClass::transfer(void* buf, size_t count)
{
    uint8_t* bytes = static_cast<uint8_t*>(buf);
    for (size_t i = 0; i < count; i++) {
        bytes[i] = transfer(bytes[i]);
    }
}
#endif

void SPIClass::end() { host().spi_end(bus_); }

void SPIClass::beginTransaction(SPISettings settings)
{
    clock_ = settings.clock_;
    bitOrder_ = settings.bitOrder_;
    dataMode_ = settings.dataMode_;
    apply();
#if defined(ARDUINO_ARCH_ESP32)
    if (hwCs_ && ss_ >= 0) {
        digitalWrite(static_cast<uint8_t>(ss_), LOW);
    }
#endif
}

void SPIClass::endTransaction(void)
{
#if defined(ARDUINO_ARCH_ESP32)
    if (hwCs_ && ss_ >= 0) {
        digitalWrite(static_cast<uint8_t>(ss_), HIGH);
    }
#endif
}

uint8_t SPIClass::transfer(uint8_t data) { return host().spi_transfer(bus_, data); }

uint16_t SPIClass::transfer16(uint16_t data)
{
    // Two bytes, in the configured bit order.
    uint8_t in_high;
    uint8_t in_low;
    if (bitOrder_ == LSBFIRST) {
        in_low = transfer(static_cast<uint8_t>(data));
        in_high = transfer(static_cast<uint8_t>(data >> 8));
    } else {
        in_high = transfer(static_cast<uint8_t>(data >> 8));
        in_low = transfer(static_cast<uint8_t>(data));
    }

    return static_cast<uint16_t>((in_high << 8) | in_low);
}

void SPIClass::setBitOrder(uint8_t bitOrder)
{
    bitOrder_ = bitOrder;
    apply();
}

void SPIClass::setDataMode(uint8_t dataMode)
{
    dataMode_ = dataMode;
    apply();
}

void SPIClass::attachInterrupt() { sim::log_warning("SPI.attachInterrupt(): SPI slave mode is not simulated; the interrupt never fires."); }

#if defined(ARDUINO_ARCH_ESP32)
void SPIClass::apply() { host().spi_settings(bus_, clock_, bitOrder_, dataMode_ & 3); }
#else
void SPIClass::apply() { host().spi_settings(bus_, clock_, bitOrder_, (dataMode_ & SPI_MODE_MASK) >> 2); }
#endif
