/*
 * Wire.h - the I2C master API of the Arduino AVR core (TwoWire), written for ArduSim.
 * Transfers go to the simulator's bus, which routes them to the device with the
 * addressed 7-bit address on the board's SDA/SCL nets and charges 9 bit times per byte
 * at the Wire clock. As on an ATmega328P: 32-byte buffers, write() returns 0 once the
 * buffer is full, endTransmission() returns 2 for a NACK on the address and 3 for a NACK
 * on data, and requestFrom() returns 0 when nobody answers. Slave mode (begin(address),
 * onReceive, onRequest) is not simulated and logs a warning.
 */
#ifndef TwoWire_h
#define TwoWire_h

#include <inttypes.h>

#include "Stream.h"

#define BUFFER_LENGTH 32
#define WIRE_HAS_END 1
#define WIRE_HAS_TIMEOUT 1

class TwoWire : public Stream {
public:
    TwoWire();

    void begin();
    void begin(uint8_t address);
    void begin(int address);
    void end();
    void setClock(uint32_t clock);
    void setWireTimeout(uint32_t timeout = 25000, bool reset_with_timeout = false);
    bool getWireTimeoutFlag(void);
    void clearWireTimeoutFlag(void);

    void beginTransmission(uint8_t address);
    void beginTransmission(int address);
    uint8_t endTransmission(void);
    uint8_t endTransmission(uint8_t sendStop);

    uint8_t requestFrom(uint8_t address, uint8_t quantity);
    uint8_t requestFrom(uint8_t address, uint8_t quantity, uint8_t sendStop);
    uint8_t requestFrom(uint8_t address, uint8_t quantity, uint32_t iaddress, uint8_t isize, uint8_t sendStop);
    uint8_t requestFrom(int address, int quantity);
    uint8_t requestFrom(int address, int quantity, int sendStop);

    virtual size_t write(uint8_t value) override;
    virtual size_t write(const uint8_t* data, size_t quantity) override;
    virtual int available(void) override;
    virtual int read(void) override;
    virtual int peek(void) override;
    virtual void flush(void) override;

    void onReceive(void (*function)(int));
    void onRequest(void (*function)(void));

    inline size_t write(unsigned long n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(long n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(unsigned int n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(int n) { return write(static_cast<uint8_t>(n)); }
    using Print::write;

private:
    uint8_t rxBuffer_[BUFFER_LENGTH];
    uint8_t rxIndex_;
    uint8_t rxLength_;
    uint8_t txAddress_;
    uint8_t txBuffer_[BUFFER_LENGTH];
    uint8_t txLength_;
    bool transmitting_;
    bool overflowReported_;
};

extern TwoWire Wire;

#endif
