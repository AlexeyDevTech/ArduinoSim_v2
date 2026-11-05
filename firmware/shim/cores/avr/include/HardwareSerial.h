/*
 * HardwareSerial.h - Serial. The 64-byte TX ring buffer lives in the simulator,
 * drained at the baud rate in virtual time; write() blocks while it is full.
 */
#ifndef HardwareSerial_h
#define HardwareSerial_h

#include <inttypes.h>

#include "Stream.h"

#define SERIAL_TX_BUFFER_SIZE 64
#define SERIAL_RX_BUFFER_SIZE 64
#define HAVE_HWSERIAL0

#define SERIAL_5N1 0x00
#define SERIAL_6N1 0x02
#define SERIAL_7N1 0x04
#define SERIAL_8N1 0x06
#define SERIAL_5N2 0x08
#define SERIAL_6N2 0x0A
#define SERIAL_7N2 0x0C
#define SERIAL_8N2 0x0E
#define SERIAL_5E1 0x20
#define SERIAL_6E1 0x22
#define SERIAL_7E1 0x24
#define SERIAL_8E1 0x26
#define SERIAL_5E2 0x28
#define SERIAL_6E2 0x2A
#define SERIAL_7E2 0x2C
#define SERIAL_8E2 0x2E
#define SERIAL_5O1 0x30
#define SERIAL_6O1 0x32
#define SERIAL_7O1 0x34
#define SERIAL_8O1 0x36
#define SERIAL_5O2 0x38
#define SERIAL_6O2 0x3A
#define SERIAL_7O2 0x3C
#define SERIAL_8O2 0x3E

class HardwareSerial : public Stream {
public:
    constexpr explicit HardwareSerial(int32_t port) : port_(port) {}

    void begin(unsigned long baud) { begin(baud, SERIAL_8N1); }
    void begin(unsigned long baud, uint8_t config);
    void end();
    virtual int available(void) override;
    virtual int peek(void) override;
    virtual int read(void) override;
    virtual int availableForWrite(void) override;
    virtual void flush(void) override;
    virtual size_t write(uint8_t) override;
    virtual size_t write(const uint8_t* buffer, size_t size) override;
    inline size_t write(unsigned long n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(long n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(unsigned int n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(int n) { return write(static_cast<uint8_t>(n)); }
    using Print::write;
    operator bool() { return true; }

private:
    int32_t port_;
};

extern HardwareSerial Serial;
#define SERIAL_PORT_MONITOR Serial
#define SERIAL_PORT_HARDWARE Serial

#if defined(SIM_BOARD_MEGA)
#define HAVE_HWSERIAL1
#define HAVE_HWSERIAL2
#define HAVE_HWSERIAL3
extern HardwareSerial Serial1;
extern HardwareSerial Serial2;
extern HardwareSerial Serial3;
#define SERIAL_PORT_HARDWARE1 Serial1
#define SERIAL_PORT_HARDWARE2 Serial2
#define SERIAL_PORT_HARDWARE3 Serial3
#define SERIAL_PORT_HARDWARE_OPEN Serial1
#endif

#endif /* HardwareSerial_h */
