// avr-libc <stdlib.h> conversions that the UCRT does not export (itoa and ltoa it does).
#include <Arduino.h>

#include <stdio.h>

extern "C" {

char* ultoa(unsigned long value, char* buffer, int radix)
{
    if (radix < 2 || radix > 36) {
        buffer[0] = 0;
        return buffer;
    }
    char digits[8 * sizeof(unsigned long) + 1];
    int n = 0;
    do {
        unsigned long d = value % static_cast<unsigned long>(radix);
        digits[n++] = static_cast<char>(d < 10 ? '0' + d : 'a' + d - 10);
        value /= static_cast<unsigned long>(radix);
    } while (value != 0);
    for (int i = 0; i < n; i++) {
        buffer[i] = digits[n - 1 - i];
    }
    buffer[n] = 0;
    return buffer;
}

char* utoa(unsigned int value, char* buffer, int radix) { return ultoa(value, buffer, radix); }

char* dtostrf(double val, signed char width, unsigned char prec, char* sout)
{
    // avr-libc writes at least width characters; the caller sizes the buffer.
    sprintf(sout, "%*.*f", static_cast<int>(width), static_cast<int>(prec), val);
    return sout;
}

}  // extern "C"
