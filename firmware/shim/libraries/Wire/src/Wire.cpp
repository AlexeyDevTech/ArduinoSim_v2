/* Wire.cpp - TwoWire over the simulator's I2C master calls (see Wire.h). */
#include "Wire.h"

#include <sim/host.h>

using sim::host;

TwoWire::TwoWire(int32_t bus)
    : bus_(bus), rxBuffer_{}, rxIndex_(0), rxLength_(0), txAddress_(0), txBuffer_{}, txLength_(0), transmitting_(false), overflowReported_(false)
{
}

#if defined(ARDUINO_ARCH_ESP32)
bool TwoWire::begin() { return begin(sda_, scl_, 0); }

bool TwoWire::begin(int sda, int scl, uint32_t frequency)
{
    rxIndex_ = 0;
    rxLength_ = 0;
    txLength_ = 0;
    sda_ = sda;
    scl_ = scl;
    host().i2c_begin(bus_, sda, scl);
    if (frequency != 0) {
        setClock(frequency);
    }
    return true;
}

bool TwoWire::begin(uint8_t address, int sda, int scl, uint32_t frequency)
{
    (void)address;
    sim::log_warning("Wire.begin(address, sda, scl, frequency): I2C slave mode is not simulated; the board stays a master.");
    return begin(sda, scl, frequency);
}

bool TwoWire::setPins(int sda, int scl)
{
    sda_ = sda;
    scl_ = scl;
    return true;
}

bool TwoWire::end()
{
    host().i2c_end(bus_);
    return true;
}

bool TwoWire::setClock(uint32_t clock)
{
    clock_ = clock;
    host().i2c_set_clock(bus_, clock);
    return true;
}
#else
#if defined(ARDUINO_ARCH_ESP8266)
void TwoWire::begin() { begin(sda_, scl_); }

void TwoWire::begin(int sda, int scl)
{
    rxIndex_ = 0;
    rxLength_ = 0;
    txLength_ = 0;
    sda_ = sda;
    scl_ = scl;
    host().i2c_begin(bus_, sda, scl);
}

void TwoWire::begin(int sda, int scl, uint8_t)
{
    sim::log_warning("Wire.begin(sda, scl, address): I2C slave mode is not simulated; the board stays a master.");
    begin(sda, scl);
}
#else
void TwoWire::begin()
{
    rxIndex_ = 0;
    rxLength_ = 0;
    txLength_ = 0;
    host().i2c_begin(bus_, SIM_PIN_DEFAULT, SIM_PIN_DEFAULT);
}
#endif

void TwoWire::end() { host().i2c_end(bus_); }

void TwoWire::setClock(uint32_t clock) { host().i2c_set_clock(bus_, clock); }
#endif

void TwoWire::begin(uint8_t)
{
    sim::log_warning("Wire.begin(address): I2C slave mode is not simulated; the board stays a master.");
    (void)begin();
}

void TwoWire::begin(int address) { begin(static_cast<uint8_t>(address)); }

// The simulated bus never hangs, so the timeout is accepted and never fires.
void TwoWire::setWireTimeout(uint32_t, bool) {}

bool TwoWire::getWireTimeoutFlag(void) { return false; }

void TwoWire::clearWireTimeoutFlag(void) {}

void TwoWire::beginTransmission(uint8_t address)
{
    transmitting_ = true;
    txAddress_ = address;
    txLength_ = 0;
}

void TwoWire::beginTransmission(int address) { beginTransmission(static_cast<uint8_t>(address)); }

uint8_t TwoWire::endTransmission(void) { return endTransmission(static_cast<uint8_t>(true)); }

uint8_t TwoWire::endTransmission(uint8_t sendStop)
{
    const SimHostApi& api = host();
    uint8_t result = 0;
    if (!api.i2c_start(bus_, txAddress_, 0)) {
        result = 2;
    } else {
        for (uint8_t i = 0; i < txLength_; i++) {
            if (!api.i2c_write(bus_, txBuffer_[i])) {
                result = 3;
                break;
            }
        }
    }

    // As the AVR TWI driver: a NACK always ends with STOP; otherwise sendStop decides,
    // and a missing STOP makes the next start a repeated start.
    if (result != 0 || sendStop) {
        api.i2c_stop(bus_);
    }

    txLength_ = 0;
    transmitting_ = false;
    return result;
}

uint8_t TwoWire::requestFrom(uint8_t address, uint8_t quantity, uint32_t iaddress, uint8_t isize, uint8_t sendStop)
{
    if (isize > 0) {
        beginTransmission(address);
        if (isize > 3) {
            isize = 3;
        }

        while (isize-- > 0) {
            write(static_cast<uint8_t>(iaddress >> (isize * 8)));
        }

        endTransmission(false);
    }

    if (quantity > BUFFER_LENGTH) {
        quantity = BUFFER_LENGTH;
    }

    const SimHostApi& api = host();
    uint8_t read = 0;
    if (quantity > 0 && api.i2c_start(bus_, address, 1)) {
        for (; read < quantity; read++) {
            rxBuffer_[read] = api.i2c_read(bus_, read + 1 < quantity ? 1 : 0);
        }

        if (sendStop) {
            api.i2c_stop(bus_);
        }
    } else {
        api.i2c_stop(bus_);
    }

    rxIndex_ = 0;
    rxLength_ = read;
    return read;
}

uint8_t TwoWire::requestFrom(uint8_t address, uint8_t quantity, uint8_t sendStop) { return requestFrom(address, quantity, 0u, 0, sendStop); }

uint8_t TwoWire::requestFrom(uint8_t address, uint8_t quantity) { return requestFrom(address, quantity, static_cast<uint8_t>(true)); }

uint8_t TwoWire::requestFrom(int address, int quantity)
{
    return requestFrom(static_cast<uint8_t>(address), static_cast<uint8_t>(quantity), static_cast<uint8_t>(true));
}

uint8_t TwoWire::requestFrom(int address, int quantity, int sendStop)
{
    return requestFrom(static_cast<uint8_t>(address), static_cast<uint8_t>(quantity), static_cast<uint8_t>(sendStop));
}

size_t TwoWire::write(uint8_t value)
{
    if (!transmitting_) {
        sim::log_warning("Wire.write() outside beginTransmission()/endTransmission() answers a slave request, which is not simulated.");
        return 0;
    }

    if (txLength_ >= BUFFER_LENGTH) {
        if (!overflowReported_) {
            overflowReported_ = true;
            #if defined(ARDUINO_ARCH_ESP32) || defined(ARDUINO_ARCH_ESP8266)
            sim::log_warning("Wire: more than 128 bytes in one transmission; as on the ESP cores the extra bytes are dropped.");
#else
            sim::log_warning("Wire: more than 32 bytes in one transmission; as on the AVR the extra bytes are dropped.");
#endif
        }

        setWriteError();
        return 0;
    }

    txBuffer_[txLength_++] = value;
    return 1;
}

size_t TwoWire::write(const uint8_t* data, size_t quantity)
{
    size_t written = 0;
    while (written < quantity && write(data[written]) == 1) {
        written++;
    }

    return written;
}

int TwoWire::available(void) { return rxLength_ - rxIndex_; }

int TwoWire::read(void) { return rxIndex_ < rxLength_ ? rxBuffer_[rxIndex_++] : -1; }

int TwoWire::peek(void) { return rxIndex_ < rxLength_ ? rxBuffer_[rxIndex_] : -1; }

void TwoWire::flush(void) {}

void TwoWire::onReceive(void (*)(int)) { sim::log_warning("Wire.onReceive(): I2C slave mode is not simulated; the handler is never called."); }

void TwoWire::onRequest(void (*)(void)) { sim::log_warning("Wire.onRequest(): I2C slave mode is not simulated; the handler is never called."); }

TwoWire Wire;
#if defined(ARDUINO_ARCH_ESP32)
TwoWire Wire1(1);
#endif
