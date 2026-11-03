/* Preferences.cpp - see Preferences.h. */
#include "Preferences.h"

#include <stdio.h>
#include <string.h>

#include <sim/host.h>

using sim::host;

namespace {

constexpr size_t MaxName = 15;
constexpr size_t MaxBlob = 508000;  // NVS limit for one blob
constexpr size_t TotalEntries = 630;  // a default 20 KB NVS partition, 32-byte entries

bool valid_name(const char* name) { return name != nullptr && name[0] != '\0' && strlen(name) <= MaxName; }

}  // namespace

bool Preferences::begin(const char* name, bool readOnly, const char*)
{
    if (started_) {
        return false;
    }
    if (!valid_name(name)) {
        char message[96];
        snprintf(message, sizeof message, "Preferences.begin(\"%s\"): the namespace must be 1 to 15 characters.", name != nullptr ? name : "");
        sim::log_warning(message);
        return false;
    }
    snprintf(namespace_, sizeof namespace_, "%s", name);
    started_ = true;
    readOnly_ = readOnly;
    return true;
}

void Preferences::end() { started_ = false; }

bool Preferences::clear()
{
    if (!started_ || readOnly_) {
        return false;
    }
    host().kv_clear(namespace_);
    return true;
}

bool Preferences::remove(const char* key)
{
    if (!started_ || readOnly_ || key == nullptr) {
        return false;
    }
    return host().kv_remove(namespace_, key) == 1;
}

size_t Preferences::put(const char* key, int32_t type, const void* data, size_t length)
{
    if (!started_ || readOnly_ || key == nullptr || length > MaxBlob) {
        return 0;
    }
    return host().kv_put(namespace_, key, type, static_cast<const uint8_t*>(data), static_cast<int32_t>(length)) == 0 ? length : 0;
}

bool Preferences::get(const char* key, int32_t type, void* out, size_t length)
{
    if (!started_ || key == nullptr) {
        return false;
    }
    int32_t stored = 0;
    uint8_t buffer[8];
    int32_t size = host().kv_get(namespace_, key, &stored, buffer, sizeof buffer);
    if (size != static_cast<int32_t>(length) || stored != type) {
        return false;
    }
    memcpy(out, buffer, length);
    return true;
}

size_t Preferences::putChar(const char* key, int8_t value) { return put(key, SIM_KV_I8, &value, 1); }
size_t Preferences::putUChar(const char* key, uint8_t value) { return put(key, SIM_KV_U8, &value, 1); }
size_t Preferences::putShort(const char* key, int16_t value) { return put(key, SIM_KV_I16, &value, 2); }
size_t Preferences::putUShort(const char* key, uint16_t value) { return put(key, SIM_KV_U16, &value, 2); }
size_t Preferences::putInt(const char* key, int32_t value) { return put(key, SIM_KV_I32, &value, 4); }
size_t Preferences::putUInt(const char* key, uint32_t value) { return put(key, SIM_KV_U32, &value, 4); }
size_t Preferences::putLong(const char* key, int32_t value) { return put(key, SIM_KV_I32, &value, 4); }
size_t Preferences::putULong(const char* key, uint32_t value) { return put(key, SIM_KV_U32, &value, 4); }
size_t Preferences::putLong64(const char* key, int64_t value) { return put(key, SIM_KV_I64, &value, 8); }
size_t Preferences::putULong64(const char* key, uint64_t value) { return put(key, SIM_KV_U64, &value, 8); }
size_t Preferences::putFloat(const char* key, float value) { return put(key, SIM_KV_FLOAT, &value, 4); }
size_t Preferences::putDouble(const char* key, double value) { return put(key, SIM_KV_DOUBLE, &value, 8); }

size_t Preferences::putBool(const char* key, bool value)
{
    uint8_t byte = value ? 1 : 0;
    return put(key, SIM_KV_BOOL, &byte, 1);
}

size_t Preferences::putString(const char* key, const char* value)
{
    if (value == nullptr) {
        return 0;
    }
    size_t length = strlen(value);
    return put(key, SIM_KV_STRING, value, length) == length ? length : 0;
}

size_t Preferences::putString(const char* key, String value) { return putString(key, value.c_str()); }

size_t Preferences::putBytes(const char* key, const void* value, size_t len) { return value == nullptr ? 0 : put(key, SIM_KV_BYTES, value, len); }

bool Preferences::isKey(const char* key) { return getType(key) != PT_INVALID; }

PreferenceType Preferences::getType(const char* key)
{
    if (!started_ || key == nullptr) {
        return PT_INVALID;
    }
    int32_t type = 0;
    if (host().kv_get(namespace_, key, &type, nullptr, 0) < 0) {
        return PT_INVALID;
    }
    switch (type) {
    case SIM_KV_I8: return PT_I8;
    case SIM_KV_U8:
    case SIM_KV_BOOL: return PT_U8;
    case SIM_KV_I16: return PT_I16;
    case SIM_KV_U16: return PT_U16;
    case SIM_KV_I32: return PT_I32;
    case SIM_KV_U32: return PT_U32;
    case SIM_KV_I64: return PT_I64;
    case SIM_KV_U64: return PT_U64;
    case SIM_KV_STRING: return PT_STR;
    default: return PT_BLOB;
    }
}

int8_t Preferences::getChar(const char* key, int8_t defaultValue) { int8_t v; return get(key, SIM_KV_I8, &v, 1) ? v : defaultValue; }
uint8_t Preferences::getUChar(const char* key, uint8_t defaultValue) { uint8_t v; return get(key, SIM_KV_U8, &v, 1) ? v : defaultValue; }
int16_t Preferences::getShort(const char* key, int16_t defaultValue) { int16_t v; return get(key, SIM_KV_I16, &v, 2) ? v : defaultValue; }
uint16_t Preferences::getUShort(const char* key, uint16_t defaultValue) { uint16_t v; return get(key, SIM_KV_U16, &v, 2) ? v : defaultValue; }
int32_t Preferences::getInt(const char* key, int32_t defaultValue) { int32_t v; return get(key, SIM_KV_I32, &v, 4) ? v : defaultValue; }
uint32_t Preferences::getUInt(const char* key, uint32_t defaultValue) { uint32_t v; return get(key, SIM_KV_U32, &v, 4) ? v : defaultValue; }
int32_t Preferences::getLong(const char* key, int32_t defaultValue) { return getInt(key, defaultValue); }
uint32_t Preferences::getULong(const char* key, uint32_t defaultValue) { return getUInt(key, defaultValue); }
int64_t Preferences::getLong64(const char* key, int64_t defaultValue) { int64_t v; return get(key, SIM_KV_I64, &v, 8) ? v : defaultValue; }
uint64_t Preferences::getULong64(const char* key, uint64_t defaultValue) { uint64_t v; return get(key, SIM_KV_U64, &v, 8) ? v : defaultValue; }
float Preferences::getFloat(const char* key, float defaultValue) { float v; return get(key, SIM_KV_FLOAT, &v, 4) ? v : defaultValue; }
double Preferences::getDouble(const char* key, double defaultValue) { double v; return get(key, SIM_KV_DOUBLE, &v, 8) ? v : defaultValue; }

bool Preferences::getBool(const char* key, bool defaultValue)
{
    uint8_t v;
    return get(key, SIM_KV_BOOL, &v, 1) ? v != 0 : defaultValue;
}

size_t Preferences::getString(const char* key, char* value, size_t maxLen)
{
    if (!started_ || key == nullptr || value == nullptr || maxLen == 0) {
        return 0;
    }
    int32_t type = 0;
    int32_t length = host().kv_get(namespace_, key, &type, reinterpret_cast<uint8_t*>(value), static_cast<int32_t>(maxLen - 1));
    if (length < 0 || type != SIM_KV_STRING || static_cast<size_t>(length) + 1 > maxLen) {
        return 0;
    }
    value[length] = '\0';
    return static_cast<size_t>(length) + 1;
}

String Preferences::getString(const char* key, String defaultValue)
{
    if (!started_ || key == nullptr) {
        return defaultValue;
    }
    int32_t type = 0;
    int32_t length = host().kv_get(namespace_, key, &type, nullptr, 0);
    if (length < 0 || type != SIM_KV_STRING) {
        return defaultValue;
    }
    char* text = static_cast<char*>(malloc(static_cast<size_t>(length) + 1));
    if (text == nullptr) {
        return defaultValue;
    }
    host().kv_get(namespace_, key, &type, reinterpret_cast<uint8_t*>(text), length);
    text[length] = '\0';
    String result(text);
    free(text);
    return result;
}

size_t Preferences::getBytesLength(const char* key)
{
    if (!started_ || key == nullptr) {
        return 0;
    }
    int32_t type = 0;
    int32_t length = host().kv_get(namespace_, key, &type, nullptr, 0);
    return length < 0 ? 0 : static_cast<size_t>(length);
}

size_t Preferences::getBytes(const char* key, void* buf, size_t maxLen)
{
    size_t length = getBytesLength(key);
    if (length == 0 || buf == nullptr || length > maxLen) {
        return 0;
    }
    int32_t type = 0;
    host().kv_get(namespace_, key, &type, static_cast<uint8_t*>(buf), static_cast<int32_t>(length));
    return length;
}

// Free entries of the whole NVS partition are not tracked; the default partition's size is returned.
size_t Preferences::freeEntries()
{
    static bool warned = false;
    if (!warned) {
        warned = true;
        sim::log_warning("Preferences.freeEntries(): NVS space is not tracked; it returns the size of an empty default partition.");
    }
    return started_ ? TotalEntries : 0;
}