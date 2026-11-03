/*
 * SPI.h - the SPI master API (SPIClass, SPISettings), written for ArduSim.
 * Transfers go to the simulator's SPI bus: the device on the bus's MOSI/SCK nets whose CS net
 * is LOW receives the byte full-duplex, and nobody selected reads 0xFF. A byte costs 8 bit times
 * at the clock the hardware would actually use.
 *
 * AVR core: constants keep their AVR values, so sketches that store or combine them behave the
 * same. ESP32 core (3.x API): SPIClass(VSPI or HSPI), begin(sck, miso, mosi, ss) on any pins,
 * setHwCs(), setFrequency(), the write and transferBytes helpers; SPI is the VSPI bus.
 */
#ifndef _SPI_H_INCLUDED
#define _SPI_H_INCLUDED

#include <Arduino.h>

#define SPI_HAS_TRANSACTION 1
#define SPI_HAS_NOTUSINGINTERRUPT 1
#define SPI_ATOMIC_VERSION 1

#if defined(ARDUINO_ARCH_ESP32) || defined(ARDUINO_ARCH_ESP8266)
#define SIM_SPI_ESP 1
#if defined(ARDUINO_ARCH_ESP32)
#define FSPI 1
#define HSPI 2
#define VSPI 3

#define SPI_MODE0 0
#define SPI_MODE1 1
#define SPI_MODE2 2
#define SPI_MODE3 3
#else
#define SPI_MODE0 0x00
#define SPI_MODE1 0x01
#define SPI_MODE2 0x10
#define SPI_MODE3 0x11
#endif

/* The core's dividers mimic the AVR speeds (16 MHz / n). */
#define SPI_CLOCK_DIV2 2
#define SPI_CLOCK_DIV4 4
#define SPI_CLOCK_DIV8 8
#define SPI_CLOCK_DIV16 16
#define SPI_CLOCK_DIV32 32
#define SPI_CLOCK_DIV64 64
#define SPI_CLOCK_DIV128 128
#else
#define SPI_CLOCK_DIV4 0x00
#define SPI_CLOCK_DIV16 0x01
#define SPI_CLOCK_DIV64 0x02
#define SPI_CLOCK_DIV128 0x03
#define SPI_CLOCK_DIV2 0x04
#define SPI_CLOCK_DIV8 0x05
#define SPI_CLOCK_DIV32 0x06

#define SPI_MODE0 0x00
#define SPI_MODE1 0x04
#define SPI_MODE2 0x08
#define SPI_MODE3 0x0C

#define SPI_MODE_MASK 0x0C
#define SPI_CLOCK_MASK 0x03
#define SPI_2XCLOCK_MASK 0x01
#endif

class SPISettings {
public:
    SPISettings(uint32_t clock, uint8_t bitOrder, uint8_t dataMode) : clock_(clock), bitOrder_(bitOrder), dataMode_(dataMode) {}
    SPISettings() : clock_(4000000), bitOrder_(MSBFIRST), dataMode_(SPI_MODE0) {}

private:
    uint32_t clock_;
    uint8_t bitOrder_;
    uint8_t dataMode_;
    friend class SPIClass;
};

class SPIClass {
public:
#if defined(SIM_SPI_ESP)
#if defined(ARDUINO_ARCH_ESP32)
    /// VSPI is bus 0 (the default SPI), HSPI bus 1.
    explicit SPIClass(uint8_t spi_bus = HSPI) : bus_(spi_bus == VSPI || spi_bus == FSPI ? 0 : 1), clock_(1000000), bitOrder_(MSBFIRST), dataMode_(SPI_MODE0) {}

    void begin(int8_t sck = -1, int8_t miso = -1, int8_t mosi = -1, int8_t ss = -1);
#else
    /// The ESP8266 has one usable hardware SPI (HSPI) on fixed pins: SCK 14, MISO 12, MOSI 13, SS 15.
    SPIClass() : bus_(0), clock_(1000000), bitOrder_(MSBFIRST), dataMode_(SPI_MODE0) {}

    void begin();
    bool pins(int8_t sck, int8_t miso, int8_t mosi, int8_t ss);
#endif
    void setHwCs(bool use) { hwCs_ = use; }
    void setFrequency(uint32_t freq);
    void setClockDivider(uint32_t clockDiv);
    uint32_t getClockDivider() { return 16000000u / (clock_ == 0 ? 1 : clock_); }
    int8_t pinSS() { return ss_; }
    uint32_t transfer32(uint32_t data);
    void transferBytes(const uint8_t* data, uint8_t* out, uint32_t size);
    void transferBits(uint32_t data, uint32_t* out, uint8_t bits);
    void write(uint8_t data) { (void)transfer(data); }
    void write16(uint16_t data) { (void)transfer16(data); }
    void write32(uint32_t data) { (void)transfer32(data); }
    void writeBytes(const uint8_t* data, uint32_t size);
    void writePixels(const void* data, uint32_t size);
    void writePattern(const uint8_t* data, uint8_t size, uint32_t repeat);
    void transfer(void* buf, uint32_t count);
#else
    /// Bus 0 is SPI; boards with a second SPI bus number it 1.
    constexpr explicit SPIClass(int32_t bus = 0) : bus_(bus), clock_(4000000), bitOrder_(MSBFIRST), dataMode_(SPI_MODE0) {}

    void begin();
    void setClockDivider(uint8_t clockDiv);
    void transfer(void* buf, size_t count);
#endif

    void end();
    void beginTransaction(SPISettings settings);
    void endTransaction(void);
    uint8_t transfer(uint8_t data);
    uint16_t transfer16(uint16_t data);

    void setBitOrder(uint8_t bitOrder);
    void setDataMode(uint8_t dataMode);

    // Interrupts do not run inside a transfer here, so these only keep sketches compiling.
    void usingInterrupt(uint8_t interruptNumber) { (void)interruptNumber; }
    void notUsingInterrupt(uint8_t interruptNumber) { (void)interruptNumber; }
    void attachInterrupt();
    void detachInterrupt() {}

private:
    void apply();

    int32_t bus_;
    uint32_t clock_;
    uint8_t bitOrder_;
    uint8_t dataMode_;
#if defined(SIM_SPI_ESP)
    int8_t ss_ = -1;
    bool hwCs_ = false;
#endif
};

extern SPIClass SPI;

#endif