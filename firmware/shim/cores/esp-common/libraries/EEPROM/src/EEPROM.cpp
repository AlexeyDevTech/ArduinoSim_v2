/* EEPROM.cpp - see EEPROM.h. */
#include "EEPROM.h"

#include <stdlib.h>

#include <sim/host.h>

using sim::host;

EEPROMClass EEPROM;

namespace {

constexpr size_t FlashSector = 4096;
uint8_t g_scratch = 0;

}  // namespace

bool EEPROMClass::begin(size_t size)
{
    if (size == 0 || size > FlashSector) {
        sim::log_warning("EEPROM.begin(size): the size must be 1..4096 bytes; nothing is mapped.");
        return false;
    }
    end();
    data_ = static_cast<uint8_t*>(malloc(size));
    if (data_ == nullptr) {
        return false;
    }
    size_ = size;
    for (size_t i = 0; i < size; i++) {
        data_[i] = host().eeprom_read(static_cast<int32_t>(i));
    }
    return true;
}

bool EEPROMClass::in_range(int address, size_t count)
{
    if (data_ == nullptr) {
        static bool warned = false;
        if (!warned) {
            warned = true;
            sim::log_warning("EEPROM is used before EEPROM.begin(size); on the ESP32 core reads return 0 and writes are lost.");
        }
        return false;
    }
    return address >= 0 && static_cast<size_t>(address) + count <= size_;
}

uint8_t EEPROMClass::read(int address) { return in_range(address, 1) ? data_[address] : 0; }

void EEPROMClass::write(int address, uint8_t value)
{
    if (in_range(address, 1)) {
        data_[address] = value;
    }
}

uint8_t& EEPROMClass::operator[](int address) { return in_range(address, 1) ? data_[address] : g_scratch; }

String EEPROMClass::readString(int address)
{
    String text;
    while (in_range(address, 1) && data_[address] != 0) {
        text += static_cast<char>(data_[address++]);
    }
    return text;
}

size_t EEPROMClass::writeString(int address, const char* value)
{
    size_t length = value != nullptr ? strlen(value) : 0;
    if (!in_range(address, length + 1)) {
        return 0;
    }
    memcpy(data_ + address, value, length);
    data_[address + static_cast<int>(length)] = 0;
    return length;
}

// Writes the bytes that differ from flash, as the core's flash sector rewrite would.
bool EEPROMClass::commit()
{
    if (data_ == nullptr) {
        return false;
    }
    for (size_t i = 0; i < size_; i++) {
        if (host().eeprom_read(static_cast<int32_t>(i)) != data_[i]) {
            host().eeprom_write(static_cast<int32_t>(i), data_[i]);
        }
    }
    return true;
}

void EEPROMClass::end()
{
    free(data_);
    data_ = nullptr;
    size_ = 0;
}