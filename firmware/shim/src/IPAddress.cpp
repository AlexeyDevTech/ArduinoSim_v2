/* IPAddress.cpp - see IPAddress.h. */
#include "IPAddress.h"

#include <stdio.h>

#include "Print.h"

const IPAddress INADDR_NONE(0, 0, 0, 0);
const IPAddress IN_ADDR_ANY(0, 0, 0, 0);

bool IPAddress::fromString(const char* address)
{
    uint32_t octets[4] = {0, 0, 0, 0};
    int part = 0;
    bool digit = false;
    for (const char* c = address; *c != '\0'; c++) {
        if (*c >= '0' && *c <= '9') {
            octets[part] = octets[part] * 10 + static_cast<uint32_t>(*c - '0');
            if (octets[part] > 255) {
                return false;
            }
            digit = true;
        } else if (*c == '.' && digit && part < 3) {
            part++;
            digit = false;
        } else {
            return false;
        }
    }
    if (part != 3 || !digit) {
        return false;
    }
    *this = IPAddress(static_cast<uint8_t>(octets[0]), static_cast<uint8_t>(octets[1]), static_cast<uint8_t>(octets[2]), static_cast<uint8_t>(octets[3]));
    return true;
}

String IPAddress::toString() const
{
    char text[16];
    snprintf(text, sizeof text, "%u.%u.%u.%u", (*this)[0], (*this)[1], (*this)[2], (*this)[3]);
    return String(text);
}

size_t IPAddress::printTo(Print& p) const { return p.print(toString()); }