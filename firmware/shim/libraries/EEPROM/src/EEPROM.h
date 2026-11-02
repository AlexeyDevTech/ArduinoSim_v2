/*
 * EEPROM.h - the EEPROM library API of the Arduino AVR core, written for ArduSim.
 * Contents persist per project in eeprom.bin. get/put copy sizeof(T) bytes, and
 * sizeof(int) is 4 here (2 on AVR), so layouts that store int differ from a real board.
 */
#ifndef EEPROM_h
#define EEPROM_h

#include <avr/eeprom.h>
#include <avr/io.h>
#include <inttypes.h>

struct EERef {
    EERef(const int index) : index(index) {}

    uint8_t operator*() const { return sim_eeprom_read(index); }
    operator uint8_t() const { return **this; }

    EERef& operator=(const EERef& ref) { return *this = *ref; }
    EERef& operator=(uint8_t in)
    {
        sim_eeprom_write(index, in);
        return *this;
    }
    EERef& operator+=(uint8_t in) { return *this = static_cast<uint8_t>(**this + in); }
    EERef& operator-=(uint8_t in) { return *this = static_cast<uint8_t>(**this - in); }
    EERef& operator*=(uint8_t in) { return *this = static_cast<uint8_t>(**this * in); }
    EERef& operator/=(uint8_t in) { return *this = static_cast<uint8_t>(**this / in); }
    EERef& operator^=(uint8_t in) { return *this = static_cast<uint8_t>(**this ^ in); }
    EERef& operator%=(uint8_t in) { return *this = static_cast<uint8_t>(**this % in); }
    EERef& operator&=(uint8_t in) { return *this = static_cast<uint8_t>(**this & in); }
    EERef& operator|=(uint8_t in) { return *this = static_cast<uint8_t>(**this | in); }
    EERef& operator<<=(uint8_t in) { return *this = static_cast<uint8_t>(**this << in); }
    EERef& operator>>=(uint8_t in) { return *this = static_cast<uint8_t>(**this >> in); }

    EERef& update(uint8_t in)
    {
        sim_eeprom_update(index, in);
        return *this;
    }

    EERef& operator++() { return *this += 1; }
    EERef& operator--() { return *this -= 1; }
    uint8_t operator++(int)
    {
        uint8_t ret = **this;
        ++(*this);
        return ret;
    }
    uint8_t operator--(int)
    {
        uint8_t ret = **this;
        --(*this);
        return ret;
    }

    int index;
};

struct EEPtr {
    EEPtr(const int index) : index(index) {}

    operator int() const { return index; }
    EEPtr& operator=(int in)
    {
        index = in;
        return *this;
    }
    bool operator!=(const EEPtr& ptr) { return index != ptr.index; }
    EERef operator*() { return index; }
    EEPtr& operator++() { return ++index, *this; }
    EEPtr& operator--() { return --index, *this; }
    EEPtr operator++(int) { return index++; }
    EEPtr operator--(int) { return index--; }

    int index;
};

struct EEPROMClass {
    EERef operator[](const int idx) { return idx; }
    uint8_t read(int idx) { return EERef(idx); }
    void write(int idx, uint8_t val) { (EERef(idx)) = val; }
    void update(int idx, uint8_t val) { EERef(idx).update(val); }

    EEPtr begin() { return 0x00; }
    EEPtr end() { return length(); }
    uint16_t length() { return E2END + 1; }

    template <typename T>
    T& get(int idx, T& t)
    {
        EEPtr e = idx;
        uint8_t* ptr = reinterpret_cast<uint8_t*>(&t);
        for (int count = sizeof(T); count; --count, ++e) {
            *ptr++ = *e;
        }
        return t;
    }

    template <typename T>
    const T& put(int idx, const T& t)
    {
        EEPtr e = idx;
        const uint8_t* ptr = reinterpret_cast<const uint8_t*>(&t);
        for (int count = sizeof(T); count; --count, ++e) {
            (*e).update(*ptr++);
        }
        return t;
    }
};

static EEPROMClass EEPROM;

#endif /* EEPROM_h */
