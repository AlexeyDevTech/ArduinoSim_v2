/* SPI.cpp - SPIClass over the simulator's SPI master calls (see SPI.h). */
#include "SPI.h"

#include <sim/host.h>

using sim::host;

SPIClass SPI;

uint32_t SPIClass::clock_ = 4000000;
uint8_t SPIClass::bitOrder_ = MSBFIRST;
uint8_t SPIClass::dataMode_ = SPI_MODE0;

void SPIClass::begin()
{
    host().spi_begin();
    apply();
}

void SPIClass::end() { host().spi_end(); }

void SPIClass::beginTransaction(SPISettings settings)
{
    clock_ = settings.clock_;
    bitOrder_ = settings.bitOrder_;
    dataMode_ = settings.dataMode_;
    apply();
}

void SPIClass::endTransaction(void) {}

uint8_t SPIClass::transfer(uint8_t data) { return host().spi_transfer(data); }

uint16_t SPIClass::transfer16(uint16_t data)
{
    // As the AVR core: two bytes, in the configured bit order.
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

void SPIClass::transfer(void* buf, size_t count)
{
    uint8_t* bytes = static_cast<uint8_t*>(buf);
    for (size_t i = 0; i < count; i++) {
        bytes[i] = transfer(bytes[i]);
    }
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

void SPIClass::setClockDivider(uint8_t clockDiv)
{
    static const uint8_t dividers[8] = {4, 16, 64, 128, 2, 8, 32, 64};
    clock_ = F_CPU / dividers[clockDiv & 0x07];
    apply();
}

void SPIClass::attachInterrupt() { sim::log_warning("SPI.attachInterrupt(): SPI slave mode is not simulated; the interrupt never fires."); }

void SPIClass::apply() { host().spi_settings(clock_, bitOrder_, (dataMode_ & SPI_MODE_MASK) >> 2); }
