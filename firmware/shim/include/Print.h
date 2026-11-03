/* Print.h - base class for text output (API of the AVR core). */
#ifndef Print_h
#define Print_h

#include <inttypes.h>
#include <stdio.h>

#include "Printable.h"
#include "WString.h"

#define DEC 10
#define HEX 16
#define OCT 8
#ifdef BIN
#undef BIN
#endif
#define BIN 2

class Print {
public:
    constexpr Print() : write_error_(0) {}

    int getWriteError() { return write_error_; }
    void clearWriteError() { setWriteError(0); }

    virtual size_t write(uint8_t) = 0;
    size_t write(const char* str)
    {
        if (str == nullptr) {
            return 0;
        }
        return write(reinterpret_cast<const uint8_t*>(str), strlen(str));
    }
    virtual size_t write(const uint8_t* buffer, size_t size);
    size_t write(const char* buffer, size_t size) { return write(reinterpret_cast<const uint8_t*>(buffer), size); }

    virtual int availableForWrite() { return 0; }
    virtual void flush() {}

    size_t print(const __FlashStringHelper*);
    size_t print(const String&);
    size_t print(const char[]);
    size_t print(char);
    size_t print(unsigned char, int = DEC);
    size_t print(int, int = DEC);
    size_t print(unsigned int, int = DEC);
    size_t print(long, int = DEC);
    size_t print(unsigned long, int = DEC);
    size_t print(double, int = 2);
    size_t print(const Printable&);

    size_t println(const __FlashStringHelper*);
    size_t println(const String& s);
    size_t println(const char[]);
    size_t println(char);
    size_t println(unsigned char, int = DEC);
    size_t println(int, int = DEC);
    size_t println(unsigned int, int = DEC);
    size_t println(long, int = DEC);
    size_t println(unsigned long, int = DEC);
    size_t println(double, int = 2);
    size_t println(const Printable&);
    size_t println(void);

#if defined(ARDUINO_ARCH_ESP32) || defined(ARDUINO_ARCH_ESP8266)
    /* The ESP cores' Print::printf (not in the AVR core). */
    size_t printf(const char* format, ...) __attribute__((format(printf, 2, 3)));
#endif

protected:
    void setWriteError(int err = 1) { write_error_ = err; }

private:
    int write_error_;
    size_t printNumber(unsigned long, uint8_t);
    size_t printFloat(double, uint8_t);
};

#endif /* Print_h */
