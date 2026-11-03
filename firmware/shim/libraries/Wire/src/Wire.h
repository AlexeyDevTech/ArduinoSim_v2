/*
 * Wire.h - the I2C master API of the Arduino AVR core (TwoWire), written for ArduSim.
 * Transfers go to the simulator's bus, which routes them to the device with the
 * addressed 7-bit address on the board's SDA/SCL nets and charges 9 bit times per byte
 * at the Wire clock. As on an ATmega328P: 32-byte buffers, write() returns 0 once the
 * buffer is full, endTransmission() returns 2 for a NACK on the address and 3 for a NACK
 * on data, and requestFrom() returns 0 when nobody answers. Slave mode (begin(address),
 * onReceive, onRequest) is not simulated and logs a warning.
 *
 * On the ESP32 core the API is the core 3.x one: begin(sda, scl, frequency) and setPins() choose
 * pins, Wire1 is the second bus, the buffer holds 128 bytes and begin()/end()/setClock() return bool.
 */
#ifndef TwoWire_h
#define TwoWire_h

#include <inttypes.h>

#include "Stream.h"

#if defined(ARDUINO_ARCH_ESP32)
#define I2C_BUFFER_LENGTH 128
#define BUFFER_LENGTH I2C_BUFFER_LENGTH
#define SIM_WIRE_RESULT bool
#elif defined(ARDUINO_ARCH_ESP8266)
#define BUFFER_LENGTH 128
#define SIM_WIRE_RESULT void
#else
#define BUFFER_LENGTH 32
#define SIM_WIRE_RESULT void
#endif
#define WIRE_HAS_END 1
#define WIRE_HAS_TIMEOUT 1

class TwoWire : public Stream {
public:
    explicit TwoWire(int32_t bus = 0);

    SIM_WIRE_RESULT begin();
    void begin(uint8_t address);
    void begin(int address);
    SIM_WIRE_RESULT end();
    SIM_WIRE_RESULT setClock(uint32_t clock);
#if defined(ARDUINO_ARCH_ESP32)
    bool begin(int sda, int scl, uint32_t frequency = 0);
    bool begin(uint8_t address, int sda, int scl, uint32_t frequency);
    bool setPins(int sda, int scl);
    uint32_t getClock() { return clock_; }
    void setTimeOut(uint16_t timeOutMillis) { timeout_ = timeOutMillis; }
    uint16_t getTimeOut() { return timeout_; }
    size_t setBufferSize(size_t size) { return size == BUFFER_LENGTH ? size : 0; }
    size_t requestFrom(uint16_t address, size_t size, bool sendStop)
    {
        return requestFrom(static_cast<uint8_t>(address), static_cast<uint8_t>(size > 255 ? 255 : size), static_cast<uint8_t>(sendStop));
    }
#elif defined(ARDUINO_ARCH_ESP8266)
    /// The ESP8266 core's I2C is bit-banged in software, so any two GPIOs work.
    void begin(int sda, int scl);
    void begin(int sda, int scl, uint8_t address);
    void pins(int sda, int scl)
    {
        sda_ = sda;
        scl_ = scl;
    }
    void setClockStretchLimit(uint32_t) {}
#endif
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
    int32_t bus_;
#if defined(ARDUINO_ARCH_ESP32)
    int sda_ = -1;
    int scl_ = -1;
    uint32_t clock_ = 100000;
    uint16_t timeout_ = 50;
#elif defined(ARDUINO_ARCH_ESP8266)
    int sda_ = -1;
    int scl_ = -1;
#endif
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
#if defined(ARDUINO_ARCH_ESP32)
extern TwoWire Wire1;
#endif

#endif
