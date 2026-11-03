/*
 * HardwareSerial.h - the Arduino-ESP32 core's UARTs: Serial (UART0, the USB port), Serial1 and
 * Serial2 with chosen pins. The host drains a 128-byte TX FIFO at the baud rate in virtual time;
 * write() blocks while it is full. Config constants keep the core's values and are translated
 * for the host.
 */
#ifndef HardwareSerial_h
#define HardwareSerial_h

#include <inttypes.h>

#include "Stream.h"

#define SERIAL_5N1 0x8000010
#define SERIAL_6N1 0x8000014
#define SERIAL_7N1 0x8000018
#define SERIAL_8N1 0x800001c
#define SERIAL_5N2 0x8000030
#define SERIAL_6N2 0x8000034
#define SERIAL_7N2 0x8000038
#define SERIAL_8N2 0x800003c
#define SERIAL_5E1 0x8000012
#define SERIAL_6E1 0x8000016
#define SERIAL_7E1 0x800001a
#define SERIAL_8E1 0x800001e
#define SERIAL_5E2 0x8000032
#define SERIAL_6E2 0x8000036
#define SERIAL_7E2 0x800003a
#define SERIAL_8E2 0x800003e
#define SERIAL_5O1 0x8000013
#define SERIAL_6O1 0x8000017
#define SERIAL_7O1 0x800001b
#define SERIAL_8O1 0x800001f
#define SERIAL_5O2 0x8000033
#define SERIAL_6O2 0x8000037
#define SERIAL_7O2 0x800003b
#define SERIAL_8O2 0x800003f

#define SOC_UART_NUM 3
#define RX1 ((int8_t)26)
#define TX1 ((int8_t)27)
#define RX2 ((int8_t)16)
#define TX2 ((int8_t)17)

class HardwareSerial : public Stream {
public:
    constexpr explicit HardwareSerial(uint8_t uart_nr) : port_(uart_nr), baud_(0), begun_(false) {}

    void begin(unsigned long baud, uint32_t config = SERIAL_8N1, int8_t rxPin = -1, int8_t txPin = -1, bool invert = false,
               unsigned long timeout_ms = 20000UL, uint8_t rxfifo_full_thrhd = 120);
    void end();
    void updateBaudRate(unsigned long baud);
    uint32_t baudRate() { return baud_; }
    bool setPins(int8_t rxPin, int8_t txPin, int8_t ctsPin = -1, int8_t rtsPin = -1);
    size_t setRxBufferSize(size_t size) { return size; }
    size_t setTxBufferSize(size_t size) { return size; }
    void setDebugOutput(bool) {}
    void setRxInvert(bool) {}
    bool setRxFIFOFull(uint8_t) { return true; }
    bool setRxTimeout(uint8_t) { return true; }

    virtual int available(void) override;
    virtual int peek(void) override;
    virtual int read(void) override;
    size_t read(uint8_t* buffer, size_t size);
    size_t read(char* buffer, size_t size) { return read(reinterpret_cast<uint8_t*>(buffer), size); }
    virtual int availableForWrite(void) override;
    virtual void flush(void) override;
    void flush(bool txOnly) { (void)txOnly; flush(); }
    virtual size_t write(uint8_t) override;
    virtual size_t write(const uint8_t* buffer, size_t size) override;
    inline size_t write(const char* buffer, size_t size) { return write(reinterpret_cast<const uint8_t*>(buffer), size); }
    inline size_t write(const char* s) { return Print::write(s); }
    inline size_t write(unsigned long n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(long n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(unsigned int n) { return write(static_cast<uint8_t>(n)); }
    inline size_t write(int n) { return write(static_cast<uint8_t>(n)); }
    using Print::write;
    operator bool() const { return true; }

private:
    uint8_t port_;
    uint32_t baud_;
    bool begun_;
};

extern HardwareSerial Serial;
extern HardwareSerial Serial1;
extern HardwareSerial Serial2;

#endif /* HardwareSerial_h */
