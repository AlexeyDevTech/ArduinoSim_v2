/*
 * WString.h - the Arduino String class (API of the AVR core), written for ArduSim.
 */
#ifndef String_class_h
#define String_class_h
#ifdef __cplusplus

#include <avr/pgmspace.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

class __FlashStringHelper;
#define F(string_literal) (reinterpret_cast<const __FlashStringHelper*>(PSTR(string_literal)))

class StringSumHelper;

class String {
public:
    String(const char* cstr = "");
    String(const char* cstr, unsigned int length);
    String(const uint8_t* cstr, unsigned int length) : String(reinterpret_cast<const char*>(cstr), length) {}
    String(const String& str);
    String(const __FlashStringHelper* str);
    String(String&& rval) noexcept;
    explicit String(char c);
    explicit String(unsigned char value, unsigned char base = 10);
    explicit String(int value, unsigned char base = 10);
    explicit String(unsigned int value, unsigned char base = 10);
    explicit String(long value, unsigned char base = 10);
    explicit String(unsigned long value, unsigned char base = 10);
    explicit String(float value, unsigned char decimalPlaces = 2);
    explicit String(double value, unsigned char decimalPlaces = 2);
    ~String();

    bool reserve(unsigned int size);
    unsigned int length() const { return len_; }
    bool isEmpty() const { return len_ == 0; }

    String& operator=(const String& rhs);
    String& operator=(const char* cstr);
    String& operator=(const __FlashStringHelper* str);
    String& operator=(String&& rval) noexcept;

    bool concat(const String& str);
    bool concat(const char* cstr);
    bool concat(const char* cstr, unsigned int length);
    bool concat(const uint8_t* cstr, unsigned int length) { return concat(reinterpret_cast<const char*>(cstr), length); }
    bool concat(char c);
    bool concat(unsigned char num);
    bool concat(int num);
    bool concat(unsigned int num);
    bool concat(long num);
    bool concat(unsigned long num);
    bool concat(float num);
    bool concat(double num);
    bool concat(const __FlashStringHelper* str);

    template <typename T>
    String& operator+=(const T& rhs)
    {
        concat(rhs);
        return *this;
    }
    String& operator+=(const char* cstr)
    {
        concat(cstr);
        return *this;
    }

    friend StringSumHelper& operator+(const StringSumHelper& lhs, const String& rhs);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, const char* cstr);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, char c);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, unsigned char num);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, int num);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, unsigned int num);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, long num);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, unsigned long num);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, float num);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, double num);
    friend StringSumHelper& operator+(const StringSumHelper& lhs, const __FlashStringHelper* rhs);

    explicit operator bool() const { return buffer_ != nullptr; }

    int compareTo(const String& s) const;
    int compareTo(const char* cstr) const;
    bool equals(const String& s) const;
    bool equals(const char* cstr) const;
    bool operator==(const String& rhs) const { return equals(rhs); }
    bool operator==(const char* cstr) const { return equals(cstr); }
    bool operator!=(const String& rhs) const { return !equals(rhs); }
    bool operator!=(const char* cstr) const { return !equals(cstr); }
    bool operator<(const String& rhs) const { return compareTo(rhs) < 0; }
    bool operator>(const String& rhs) const { return compareTo(rhs) > 0; }
    bool operator<=(const String& rhs) const { return compareTo(rhs) <= 0; }
    bool operator>=(const String& rhs) const { return compareTo(rhs) >= 0; }
    bool equalsIgnoreCase(const String& s) const;
    bool startsWith(const String& prefix) const;
    bool startsWith(const String& prefix, unsigned int offset) const;
    bool endsWith(const String& suffix) const;

    char charAt(unsigned int index) const;
    void setCharAt(unsigned int index, char c);
    char operator[](unsigned int index) const;
    char& operator[](unsigned int index);
    void getBytes(unsigned char* buf, unsigned int bufsize, unsigned int index = 0) const;
    void toCharArray(char* buf, unsigned int bufsize, unsigned int index = 0) const
    {
        getBytes(reinterpret_cast<unsigned char*>(buf), bufsize, index);
    }
    const char* c_str() const { return buffer_ != nullptr ? buffer_ : ""; }
    char* begin() { return buffer_; }
    char* end() { return buffer_ + len_; }
    const char* begin() const { return c_str(); }
    const char* end() const { return c_str() + len_; }

    int indexOf(char ch) const { return indexOf(ch, 0); }
    int indexOf(char ch, unsigned int fromIndex) const;
    int indexOf(const String& str) const { return indexOf(str, 0); }
    int indexOf(const String& str, unsigned int fromIndex) const;
    int lastIndexOf(char ch) const;
    int lastIndexOf(char ch, unsigned int fromIndex) const;
    int lastIndexOf(const String& str) const;
    int lastIndexOf(const String& str, unsigned int fromIndex) const;
    String substring(unsigned int beginIndex) const { return substring(beginIndex, len_); }
    String substring(unsigned int beginIndex, unsigned int endIndex) const;

    void replace(char find, char replace);
    void replace(const String& find, const String& replace);
    void remove(unsigned int index);
    void remove(unsigned int index, unsigned int count);
    void toLowerCase();
    void toUpperCase();
    void trim();

    long toInt() const;
    float toFloat() const;
    double toDouble() const;

protected:
    char* buffer_ = nullptr;
    unsigned int capacity_ = 0;
    unsigned int len_ = 0;

private:
    void invalidate();
    bool changeBuffer(unsigned int maxStrLen);
    String& copy(const char* cstr, unsigned int length);
    void move(String& rhs) noexcept;
};

/* Lets "text" + String(x) + 42 build one String, as on Arduino. */
class StringSumHelper : public String {
public:
    StringSumHelper(const String& s) : String(s) {}
    StringSumHelper(const char* p) : String(p) {}
    StringSumHelper(char c) : String(c) {}
    StringSumHelper(unsigned char num) : String(num) {}
    StringSumHelper(int num) : String(num) {}
    StringSumHelper(unsigned int num) : String(num) {}
    StringSumHelper(long num) : String(num) {}
    StringSumHelper(unsigned long num) : String(num) {}
    StringSumHelper(float num) : String(num) {}
    StringSumHelper(double num) : String(num) {}
};

inline bool operator==(const char* lhs, const String& rhs) { return rhs.equals(lhs); }
inline bool operator!=(const char* lhs, const String& rhs) { return !rhs.equals(lhs); }

#endif /* __cplusplus */
#endif /* String_class_h */
