// Arduino String, written for ArduSim against the AVR core's documented behaviour.
#include "WString.h"

#include <Arduino.h>
#include <ctype.h>
#include <stdio.h>

namespace {
char g_dummy_writable_char;

const char* format_float(char* buf, size_t size, double value, unsigned char decimals, int width)
{
    snprintf(buf, size, "%*.*f", width, static_cast<int>(decimals), value);
    return buf;
}
}  // namespace

String::String(const char* cstr)
{
    if (cstr != nullptr) {
        copy(cstr, static_cast<unsigned int>(strlen(cstr)));
    }
}

String::String(const char* cstr, unsigned int length)
{
    if (cstr != nullptr) {
        copy(cstr, length);
    }
}

String::String(const String& value) { *this = value; }

String::String(const __FlashStringHelper* pstr) { *this = pstr; }

String::String(String&& rval) noexcept { move(rval); }

String::String(char c)
{
    char buf[2] = {c, 0};
    *this = buf;
}

String::String(unsigned char value, unsigned char base)
{
    char buf[1 + 8 * sizeof(unsigned char)];
    *this = utoa(value, buf, base);
}

String::String(int value, unsigned char base)
{
    char buf[2 + 8 * sizeof(int)];
    *this = itoa(value, buf, base);
}

String::String(unsigned int value, unsigned char base)
{
    char buf[1 + 8 * sizeof(unsigned int)];
    *this = utoa(value, buf, base);
}

String::String(long value, unsigned char base)
{
    char buf[2 + 8 * sizeof(long)];
    *this = ltoa(value, buf, base);
}

String::String(unsigned long value, unsigned char base)
{
    char buf[1 + 8 * sizeof(unsigned long)];
    *this = ultoa(value, buf, base);
}

String::String(float value, unsigned char decimalPlaces)
{
    char buf[64];
    *this = format_float(buf, sizeof buf, value, decimalPlaces, decimalPlaces + 2);
}

String::String(double value, unsigned char decimalPlaces)
{
    char buf[64];
    *this = format_float(buf, sizeof buf, value, decimalPlaces, decimalPlaces + 2);
}

String::~String() { free(buffer_); }

void String::invalidate()
{
    free(buffer_);
    buffer_ = nullptr;
    capacity_ = len_ = 0;
}

bool String::reserve(unsigned int size)
{
    if (buffer_ != nullptr && capacity_ >= size) {
        return true;
    }
    if (changeBuffer(size)) {
        if (len_ == 0) {
            buffer_[0] = 0;
        }
        return true;
    }
    return false;
}

bool String::changeBuffer(unsigned int maxStrLen)
{
    char* grown = static_cast<char*>(realloc(buffer_, maxStrLen + 1));
    if (grown == nullptr) {
        return false;
    }
    buffer_ = grown;
    capacity_ = maxStrLen;
    return true;
}

String& String::copy(const char* cstr, unsigned int length)
{
    if (!reserve(length)) {
        invalidate();
        return *this;
    }
    len_ = length;
    memmove(buffer_, cstr, length);
    buffer_[len_] = 0;
    return *this;
}

void String::move(String& rhs) noexcept
{
    free(buffer_);
    buffer_ = rhs.buffer_;
    capacity_ = rhs.capacity_;
    len_ = rhs.len_;
    rhs.buffer_ = nullptr;
    rhs.capacity_ = rhs.len_ = 0;
}

String& String::operator=(const String& rhs)
{
    if (this == &rhs) {
        return *this;
    }
    if (rhs.buffer_ != nullptr) {
        copy(rhs.buffer_, rhs.len_);
    } else {
        invalidate();
    }
    return *this;
}

String& String::operator=(String&& rval) noexcept
{
    if (this != &rval) {
        move(rval);
    }
    return *this;
}

String& String::operator=(const char* cstr)
{
    if (cstr != nullptr) {
        copy(cstr, static_cast<unsigned int>(strlen(cstr)));
    } else {
        invalidate();
    }
    return *this;
}

String& String::operator=(const __FlashStringHelper* pstr) { return *this = reinterpret_cast<const char*>(pstr); }

bool String::concat(const String& s) { return concat(s.buffer_, s.len_); }

bool String::concat(const char* cstr, unsigned int length)
{
    if (cstr == nullptr) {
        return false;
    }
    if (length == 0) {
        return true;
    }
    // cstr may point into our own buffer (s += s); keep its offset across realloc.
    bool self = buffer_ != nullptr && cstr >= buffer_ && cstr < buffer_ + capacity_ + 1;
    size_t offset = self ? static_cast<size_t>(cstr - buffer_) : 0;
    unsigned int newlen = len_ + length;
    if (!reserve(newlen)) {
        return false;
    }
    memmove(buffer_ + len_, self ? buffer_ + offset : cstr, length);
    len_ = newlen;
    buffer_[len_] = 0;
    return true;
}

bool String::concat(const char* cstr)
{
    if (cstr == nullptr) {
        return false;
    }
    return concat(cstr, static_cast<unsigned int>(strlen(cstr)));
}

bool String::concat(char c)
{
    char buf[2] = {c, 0};
    return concat(buf, 1);
}

bool String::concat(unsigned char num)
{
    char buf[1 + 3 * sizeof(unsigned char)];
    return concat(utoa(num, buf, 10));
}

bool String::concat(int num)
{
    char buf[2 + 3 * sizeof(int)];
    return concat(itoa(num, buf, 10));
}

bool String::concat(unsigned int num)
{
    char buf[1 + 3 * sizeof(unsigned int)];
    return concat(utoa(num, buf, 10));
}

bool String::concat(long num)
{
    char buf[2 + 3 * sizeof(long)];
    return concat(ltoa(num, buf, 10));
}

bool String::concat(unsigned long num)
{
    char buf[1 + 3 * sizeof(unsigned long)];
    return concat(ultoa(num, buf, 10));
}

bool String::concat(float num)
{
    char buf[64];
    return concat(format_float(buf, sizeof buf, num, 2, 4));
}

bool String::concat(double num)
{
    char buf[64];
    return concat(format_float(buf, sizeof buf, num, 2, 4));
}

bool String::concat(const __FlashStringHelper* str) { return concat(reinterpret_cast<const char*>(str)); }

namespace {
template <typename T>
StringSumHelper& sum(const StringSumHelper& lhs, const T& rhs)
{
    StringSumHelper& a = const_cast<StringSumHelper&>(lhs);
    a.concat(rhs);
    return a;
}
}  // namespace

StringSumHelper& operator+(const StringSumHelper& lhs, const String& rhs)
{
    StringSumHelper& a = const_cast<StringSumHelper&>(lhs);
    if (!a.concat(rhs.buffer_, rhs.len_)) {
        a.invalidate();
    }
    return a;
}

StringSumHelper& operator+(const StringSumHelper& lhs, const char* cstr)
{
    StringSumHelper& a = const_cast<StringSumHelper&>(lhs);
    if (cstr == nullptr || !a.concat(cstr, static_cast<unsigned int>(strlen(cstr)))) {
        a.invalidate();
    }
    return a;
}

StringSumHelper& operator+(const StringSumHelper& lhs, char c) { return sum(lhs, c); }
StringSumHelper& operator+(const StringSumHelper& lhs, unsigned char num) { return sum(lhs, num); }
StringSumHelper& operator+(const StringSumHelper& lhs, int num) { return sum(lhs, num); }
StringSumHelper& operator+(const StringSumHelper& lhs, unsigned int num) { return sum(lhs, num); }
StringSumHelper& operator+(const StringSumHelper& lhs, long num) { return sum(lhs, num); }
StringSumHelper& operator+(const StringSumHelper& lhs, unsigned long num) { return sum(lhs, num); }
StringSumHelper& operator+(const StringSumHelper& lhs, float num) { return sum(lhs, num); }
StringSumHelper& operator+(const StringSumHelper& lhs, double num) { return sum(lhs, num); }
StringSumHelper& operator+(const StringSumHelper& lhs, const __FlashStringHelper* rhs) { return sum(lhs, rhs); }

int String::compareTo(const String& s) const
{
    if (buffer_ == nullptr || s.buffer_ == nullptr) {
        if (s.buffer_ != nullptr && s.len_ > 0) {
            return 0 - *reinterpret_cast<const unsigned char*>(s.buffer_);
        }
        if (buffer_ != nullptr && len_ > 0) {
            return *reinterpret_cast<const unsigned char*>(buffer_);
        }
        return 0;
    }
    return strcmp(buffer_, s.buffer_);
}

int String::compareTo(const char* cstr) const
{
    if (buffer_ == nullptr || cstr == nullptr) {
        if (cstr != nullptr && *cstr != 0) {
            return 0 - *reinterpret_cast<const unsigned char*>(cstr);
        }
        if (buffer_ != nullptr && len_ > 0) {
            return *reinterpret_cast<const unsigned char*>(buffer_);
        }
        return 0;
    }
    return strcmp(buffer_, cstr);
}

bool String::equals(const String& s) const { return len_ == s.len_ && compareTo(s) == 0; }

bool String::equals(const char* cstr) const
{
    if (len_ == 0) {
        return cstr == nullptr || *cstr == 0;
    }
    if (cstr == nullptr) {
        return buffer_[0] == 0;
    }
    return strcmp(buffer_, cstr) == 0;
}

bool String::equalsIgnoreCase(const String& s) const
{
    if (this == &s) {
        return true;
    }
    if (len_ != s.len_) {
        return false;
    }
    for (unsigned int i = 0; i < len_; i++) {
        if (tolower(static_cast<unsigned char>(buffer_[i])) != tolower(static_cast<unsigned char>(s.buffer_[i]))) {
            return false;
        }
    }
    return true;
}

bool String::startsWith(const String& prefix) const
{
    if (len_ < prefix.len_) {
        return false;
    }
    return startsWith(prefix, 0);
}

bool String::startsWith(const String& prefix, unsigned int offset) const
{
    if (prefix.len_ > len_ || offset > len_ - prefix.len_ || buffer_ == nullptr || prefix.buffer_ == nullptr) {
        return false;
    }
    return strncmp(&buffer_[offset], prefix.buffer_, prefix.len_) == 0;
}

bool String::endsWith(const String& suffix) const
{
    if (len_ < suffix.len_ || buffer_ == nullptr || suffix.buffer_ == nullptr) {
        return false;
    }
    return strcmp(&buffer_[len_ - suffix.len_], suffix.buffer_) == 0;
}

char String::charAt(unsigned int index) const { return operator[](index); }

void String::setCharAt(unsigned int index, char c)
{
    if (index < len_) {
        buffer_[index] = c;
    }
}

char& String::operator[](unsigned int index)
{
    if (index >= len_ || buffer_ == nullptr) {
        g_dummy_writable_char = 0;
        return g_dummy_writable_char;
    }
    return buffer_[index];
}

char String::operator[](unsigned int index) const
{
    if (index >= len_ || buffer_ == nullptr) {
        return 0;
    }
    return buffer_[index];
}

void String::getBytes(unsigned char* buf, unsigned int bufsize, unsigned int index) const
{
    if (bufsize == 0 || buf == nullptr) {
        return;
    }
    if (index >= len_) {
        buf[0] = 0;
        return;
    }
    unsigned int n = bufsize - 1;
    if (n > len_ - index) {
        n = len_ - index;
    }
    memcpy(buf, buffer_ + index, n);
    buf[n] = 0;
}

int String::indexOf(char ch, unsigned int fromIndex) const
{
    if (fromIndex >= len_) {
        return -1;
    }
    const char* found = strchr(buffer_ + fromIndex, ch);
    return found == nullptr ? -1 : static_cast<int>(found - buffer_);
}

int String::indexOf(const String& s, unsigned int fromIndex) const
{
    if (fromIndex >= len_) {
        return -1;
    }
    const char* found = strstr(buffer_ + fromIndex, s.c_str());
    return found == nullptr ? -1 : static_cast<int>(found - buffer_);
}

int String::lastIndexOf(char ch) const { return len_ == 0 ? -1 : lastIndexOf(ch, len_ - 1); }

int String::lastIndexOf(char ch, unsigned int fromIndex) const
{
    if (fromIndex >= len_) {
        return -1;
    }
    for (int i = static_cast<int>(fromIndex); i >= 0; i--) {
        if (buffer_[i] == ch) {
            return i;
        }
    }
    return -1;
}

int String::lastIndexOf(const String& s) const { return s.len_ > len_ ? -1 : lastIndexOf(s, len_ - s.len_); }

int String::lastIndexOf(const String& s, unsigned int fromIndex) const
{
    if (s.len_ == 0 || len_ == 0 || s.len_ > len_) {
        return -1;
    }
    if (fromIndex >= len_) {
        fromIndex = len_ - 1;
    }
    int found = -1;
    for (const char* p = buffer_; p <= buffer_ + fromIndex; p++) {
        p = strstr(p, s.buffer_);
        if (p == nullptr) {
            break;
        }
        if (static_cast<unsigned int>(p - buffer_) <= fromIndex) {
            found = static_cast<int>(p - buffer_);
        }
    }
    return found;
}

String String::substring(unsigned int left, unsigned int right) const
{
    if (left > right) {
        unsigned int t = right;
        right = left;
        left = t;
    }
    String out;
    if (left >= len_) {
        return out;
    }
    if (right > len_) {
        right = len_;
    }
    out.copy(buffer_ + left, right - left);
    return out;
}

void String::replace(char find, char replace)
{
    for (unsigned int i = 0; i < len_; i++) {
        if (buffer_[i] == find) {
            buffer_[i] = replace;
        }
    }
}

void String::replace(const String& find, const String& replace)
{
    if (len_ == 0 || find.len_ == 0) {
        return;
    }
    String result;
    result.reserve(len_);
    const char* p = buffer_;
    const char* hit;
    while ((hit = strstr(p, find.buffer_)) != nullptr) {
        result.concat(p, static_cast<unsigned int>(hit - p));
        result.concat(replace);
        p = hit + find.len_;
    }
    result.concat(p, static_cast<unsigned int>(buffer_ + len_ - p));
    *this = static_cast<String&&>(result);
}

void String::remove(unsigned int index) { remove(index, static_cast<unsigned int>(-1)); }

void String::remove(unsigned int index, unsigned int count)
{
    if (index >= len_) {
        return;
    }
    if (count > len_ - index) {
        count = len_ - index;
    }
    memmove(buffer_ + index, buffer_ + index + count, len_ - index - count);
    len_ -= count;
    buffer_[len_] = 0;
}

void String::toLowerCase()
{
    for (unsigned int i = 0; i < len_; i++) {
        buffer_[i] = static_cast<char>(tolower(static_cast<unsigned char>(buffer_[i])));
    }
}

void String::toUpperCase()
{
    for (unsigned int i = 0; i < len_; i++) {
        buffer_[i] = static_cast<char>(toupper(static_cast<unsigned char>(buffer_[i])));
    }
}

void String::trim()
{
    if (buffer_ == nullptr || len_ == 0) {
        return;
    }
    char* begin = buffer_;
    while (isspace(static_cast<unsigned char>(*begin))) {
        begin++;
    }
    char* end = buffer_ + len_ - 1;
    while (end >= begin && isspace(static_cast<unsigned char>(*end))) {
        end--;
    }
    len_ = static_cast<unsigned int>(end + 1 - begin);
    if (begin > buffer_) {
        memmove(buffer_, begin, len_);
    }
    buffer_[len_] = 0;
}

long String::toInt() const { return buffer_ != nullptr ? atol(buffer_) : 0; }

float String::toFloat() const { return static_cast<float>(toDouble()); }

double String::toDouble() const { return buffer_ != nullptr ? atof(buffer_) : 0; }
