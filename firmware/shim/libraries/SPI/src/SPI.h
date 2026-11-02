/*
 * SPI.h - the SPI master API of the Arduino AVR core (SPIClass, SPISettings), written for ArduSim.
 * Transfers go to the simulator's SPI bus: the device on the board's MOSI/SCK nets whose CS
 * net is LOW receives the byte full-duplex, and nobody selected reads 0xFF. A byte costs 8 bit
 * times at the clock the ATmega328P would actually use (F_CPU / 2..128). Constants keep their
 * AVR values, so sketches that store or combine them behave the same.
 */
#ifndef _SPI_H_INCLUDED
#define _SPI_H_INCLUDED

#include <Arduino.h>

#define SPI_HAS_TRANSACTION 1
#define SPI_HAS_NOTUSINGINTERRUPT 1
#define SPI_ATOMIC_VERSION 1

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
    static void begin();
    static void end();
    static void beginTransaction(SPISettings settings);
    static void endTransaction(void);
    static uint8_t transfer(uint8_t data);
    static uint16_t transfer16(uint16_t data);
    static void transfer(void* buf, size_t count);

    static void setBitOrder(uint8_t bitOrder);
    static void setDataMode(uint8_t dataMode);
    static void setClockDivider(uint8_t clockDiv);

    // Interrupts do not run inside a transfer here, so these only keep sketches compiling.
    static void usingInterrupt(uint8_t interruptNumber) { (void)interruptNumber; }
    static void notUsingInterrupt(uint8_t interruptNumber) { (void)interruptNumber; }
    static void attachInterrupt();
    static void detachInterrupt() {}

private:
    static void apply();

    static uint32_t clock_;
    static uint8_t bitOrder_;
    static uint8_t dataMode_;
};

extern SPIClass SPI;

#endif
