/*
 * HardwareSerial.h - the ESP8266 core's UARTs: Serial (UART0, the USB port) and Serial1 (TX only,
 * GPIO 2). The host drains a 128-byte TX FIFO at the baud rate in virtual time. Config constants
 * keep the core's values and are translated for the host.
 */
#ifndef HardwareSerial_h
#define HardwareSerial_h

#include <inttypes.h>

#include "Stream.h"

enum SerialConfig {
    SERIAL_5N1 = 0x10, SERIAL_6N1 = 0x14, SERIAL_7N1 = 0x18, SERIAL_8N1 = 0x1c,
    SERIAL_5N2 = 0x30, SERIAL_6N2 = 0x34, SERIAL_7N2 = 0x38, SERIAL_8N2 = 0x3c,
    SERIAL_5E1 = 0x12, SERIAL_6E1 = 0x16, SERIAL_7E1 = 0x1a, SERIAL_8E1 = 0x1e,
    SERIAL_5E2 = 0x32, SERIAL_6E2 = 0x36, SERIAL_7E2 = 0x3a, SERIAL_8E2 = 0x3e,
    SERIAL_5O1 = 0x13, SERIAL_6O1 = 0x17, SERIAL_7O1 = 0x1b, SERIAL_8O1 = 0x1f,
    SERIAL_5O2 = 0x33, SERIAL_6O2 = 0x37, SERIAL_7O2 = 0x3b, SERIAL_8O2 = 0x3f
};

enum SerialMode { SERIAL_FULL = 0, SERIAL_RX_ONLY = 1, SERIAL_TX_ONLY = 2 };

class HardwareSerial : public Stream {
public:
    constexpr explicit HardwareSerial(int uart_nr) : port_(uart_nr), baud_(0) {}

    void begin(unsigned long baud) { begin(baud, SERIAL_8N1, SERIAL_FULL, 1, false); }
    void begin(unsigned long baud, SerialConfig config) { begin(baud, config, SERIAL_FULL, 1, false); }
    void begin(unsigned long baud, SerialConfig config, SerialMode mode) { begin(baud, config, mode, 1, false); }
    void begin(unsigned long baud, SerialConfig config, SerialMode mode, uint8_t tx_pin, bool invert = false);
    void end();
    void updateBaudRate(unsigned long baud);
    unsigned long baudRate() { return baud_; }
    size_t setRxBufferSize(size_t size) { return size; }
    void swap() { swap(1); }
    void swap(uint8_t tx_pin);
    void set_tx(uint8_t) {}
    void pins(uint8_t, uint8_t) {}
    void setDebugOutput(bool) {}
    bool isTxEnabled() { return true; }
    bool isRxEnabled() { return port_ == 0; }
    bool hasOverrun() { return false; }

    virtual int available(void) override;
    virtual int peek(void) override;
    virtual int read(void) override;
    size_t read(char* buffer, size_t size);
    size_t read(uint8_t* buffer, size_t size) { return read(reinterpret_cast<char*>(buffer), size); }
    virtual int availableForWrite(void) override;
    virtual void flush(void) override;
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
    int port_;
    unsigned long baud_;
};

extern HardwareSerial Serial;
extern HardwareSerial Serial1;

#endif /* HardwareSerial_h */