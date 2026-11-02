// Print: number and float formatting as in the AVR core.
#include "Print.h"

#include <Arduino.h>
#include <math.h>

size_t Print::write(const uint8_t* buffer, size_t size)
{
    size_t n = 0;
    while (size-- > 0) {
        if (write(*buffer++) == 0) {
            break;
        }
        n++;
    }
    return n;
}

size_t Print::print(const __FlashStringHelper* ifsh) { return print(reinterpret_cast<const char*>(ifsh)); }

size_t Print::print(const String& s) { return write(s.c_str(), s.length()); }

size_t Print::print(const char str[]) { return write(str); }

size_t Print::print(char c) { return write(static_cast<uint8_t>(c)); }

size_t Print::print(unsigned char b, int base) { return print(static_cast<unsigned long>(b), base); }

size_t Print::print(int n, int base) { return print(static_cast<long>(n), base); }

size_t Print::print(unsigned int n, int base) { return print(static_cast<unsigned long>(n), base); }

size_t Print::print(long n, int base)
{
    if (base == 0) {
        return write(static_cast<uint8_t>(n));
    }
    if (base == 10 && n < 0) {
        size_t t = print('-');
        return printNumber(0UL - static_cast<unsigned long>(n), 10) + t;
    }
    return printNumber(static_cast<unsigned long>(n), static_cast<uint8_t>(base));
}

size_t Print::print(unsigned long n, int base)
{
    if (base == 0) {
        return write(static_cast<uint8_t>(n));
    }
    return printNumber(n, static_cast<uint8_t>(base));
}

size_t Print::print(double n, int digits) { return printFloat(n, static_cast<uint8_t>(digits)); }

size_t Print::print(const Printable& x) { return x.printTo(*this); }

size_t Print::println(void) { return write("\r\n"); }

size_t Print::println(const __FlashStringHelper* ifsh) { return print(ifsh) + println(); }
size_t Print::println(const String& s) { return print(s) + println(); }
size_t Print::println(const char c[]) { return print(c) + println(); }
size_t Print::println(char c) { return print(c) + println(); }
size_t Print::println(unsigned char b, int base) { return print(b, base) + println(); }
size_t Print::println(int num, int base) { return print(num, base) + println(); }
size_t Print::println(unsigned int num, int base) { return print(num, base) + println(); }
size_t Print::println(long num, int base) { return print(num, base) + println(); }
size_t Print::println(unsigned long num, int base) { return print(num, base) + println(); }
size_t Print::println(double num, int digits) { return print(num, digits) + println(); }
size_t Print::println(const Printable& x) { return print(x) + println(); }

size_t Print::printNumber(unsigned long n, uint8_t base)
{
    char buf[8 * sizeof(long) + 1];
    char* str = &buf[sizeof(buf) - 1];
    *str = '\0';
    if (base < 2) {
        base = 10;
    }
    do {
        char c = static_cast<char>(n % base);
        n /= base;
        *--str = static_cast<char>(c < 10 ? c + '0' : c + 'A' - 10);
    } while (n != 0);
    return write(str);
}

size_t Print::printFloat(double number, uint8_t digits)
{
    if (isnan(number)) {
        return print("nan");
    }
    if (isinf(number)) {
        return print("inf");
    }
    if (number > 4294967040.0 || number < -4294967040.0) {
        return print("ovf");  // the integer part must fit an unsigned long, as on AVR
    }

    size_t n = 0;
    if (number < 0.0) {
        n += print('-');
        number = -number;
    }

    double rounding = 0.5;
    for (uint8_t i = 0; i < digits; ++i) {
        rounding /= 10.0;
    }
    number += rounding;

    unsigned long integer = static_cast<unsigned long>(number);
    double remainder = number - static_cast<double>(integer);
    n += print(integer);
    if (digits > 0) {
        n += print('.');
    }
    while (digits-- > 0) {
        remainder *= 10.0;
        unsigned int digit = static_cast<unsigned int>(remainder);
        n += print(digit);
        remainder -= digit;
    }
    return n;
}
