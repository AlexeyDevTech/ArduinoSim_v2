/*
 * EEPROM.h - the Arduino-ESP32 EEPROM emulation, written for ArduSim. As on the core, begin(size)
 * copies up to 4096 bytes from flash into RAM, reads and writes work on that copy, and commit()
 * writes the changed bytes back; here "flash" is the project's eeprom.bin.
 */
#ifndef EEPROM_h
#define EEPROM_h

#include <Arduino.h>
#include <string.h>

class EEPROMClass {
public:
    EEPROMClass() = default;
    ~EEPROMClass() { end(); }

    bool begin(size_t size);
    uint8_t read(int address);
    void write(int address, uint8_t value);
    uint16_t length() { return static_cast<uint16_t>(size_); }
    bool commit();
    void end();
    uint8_t* getDataPtr() { return data_; }
    uint8_t& operator[](int address);

    uint8_t readByte(int address) { return read(address); }
    int8_t readChar(int address) { return static_cast<int8_t>(read(address)); }
    uint8_t readUChar(int address) { return read(address); }
    bool readBool(int address) { return read(address) != 0; }
    String readString(int address);
    size_t writeByte(int address, uint8_t value) { write(address, value); return 1; }
    size_t writeChar(int address, int8_t value) { write(address, static_cast<uint8_t>(value)); return 1; }
    size_t writeUChar(int address, uint8_t value) { write(address, value); return 1; }
    size_t writeBool(int address, bool value) { write(address, value ? 1 : 0); return 1; }
    size_t writeString(int address, const char* value);
    size_t writeString(int address, String value) { return writeString(address, value.c_str()); }

    template <typename T>
    T& get(int address, T& value)
    {
        if (in_range(address, sizeof(T))) {
            memcpy(&value, data_ + address, sizeof(T));
        }
        return value;
    }

    template <typename T>
    const T& put(int address, const T& value)
    {
        if (in_range(address, sizeof(T))) {
            memcpy(data_ + address, &value, sizeof(T));
        }
        return value;
    }

private:
    bool in_range(int address, size_t count);

    uint8_t* data_ = nullptr;
    size_t size_ = 0;
};

extern EEPROMClass EEPROM;

#endif