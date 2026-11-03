/*
 * IPAddress.h - an IPv4 address as the Arduino cores keep it: four octets in one uint32_t,
 * the first octet in the lowest byte.
 */
#ifndef IPAddress_h
#define IPAddress_h

#include <stdint.h>

#include "Printable.h"
#include "WString.h"

class IPAddress : public Printable {
public:
    IPAddress() : address_(0) {}
    IPAddress(uint8_t first, uint8_t second, uint8_t third, uint8_t fourth)
        : address_(static_cast<uint32_t>(first) | (static_cast<uint32_t>(second) << 8) | (static_cast<uint32_t>(third) << 16) | (static_cast<uint32_t>(fourth) << 24))
    {
    }
    IPAddress(uint32_t address) : address_(address) {}
    IPAddress(const uint8_t* address) : IPAddress(address[0], address[1], address[2], address[3]) {}

    bool fromString(const char* address);
    bool fromString(const String& address) { return fromString(address.c_str()); }

    operator uint32_t() const { return address_; }
    bool operator==(const IPAddress& other) const { return address_ == other.address_; }
    bool operator!=(const IPAddress& other) const { return address_ != other.address_; }
    bool operator==(const uint8_t* address) const { return *this == IPAddress(address); }
    uint8_t operator[](int index) const { return static_cast<uint8_t>(address_ >> (8 * index)); }
    IPAddress& operator=(uint32_t address)
    {
        address_ = address;
        return *this;
    }

    virtual size_t printTo(Print& p) const override;
    String toString() const;

private:
    uint32_t address_;
};

extern const IPAddress INADDR_NONE;
extern const IPAddress IN_ADDR_ANY;

#endif