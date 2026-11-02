/* Stream.h - base class for character and binary input (API of the AVR core). */
#ifndef Stream_h
#define Stream_h

#include <inttypes.h>

#include "Print.h"

enum LookaheadMode {
    SKIP_ALL,
    SKIP_NONE,
    SKIP_WHITESPACE
};

#define NO_IGNORE_CHAR '\x01'

class Stream : public Print {
protected:
    unsigned long _timeout;
    unsigned long _startMillis;
    int timedRead();
    int timedPeek();
    int peekNextDigit(LookaheadMode lookahead, bool detectDecimal);

public:
    virtual int available() = 0;
    virtual int read() = 0;
    virtual int peek() = 0;

    constexpr Stream() : _timeout(1000), _startMillis(0) {}

    void setTimeout(unsigned long timeout);
    unsigned long getTimeout(void) { return _timeout; }

    bool find(const char* target);
    bool find(const uint8_t* target) { return find(reinterpret_cast<const char*>(target)); }
    bool find(const char* target, size_t length);
    bool find(const uint8_t* target, size_t length) { return find(reinterpret_cast<const char*>(target), length); }
    bool find(char target) { return find(&target, 1); }

    bool findUntil(const char* target, const char* terminator);
    bool findUntil(const uint8_t* target, const char* terminator) { return findUntil(reinterpret_cast<const char*>(target), terminator); }
    bool findUntil(const char* target, size_t targetLen, const char* terminate, size_t termLen);
    bool findUntil(const uint8_t* target, size_t targetLen, const char* terminate, size_t termLen)
    {
        return findUntil(reinterpret_cast<const char*>(target), targetLen, terminate, termLen);
    }

    long parseInt(LookaheadMode lookahead = SKIP_ALL, char ignore = NO_IGNORE_CHAR);
    float parseFloat(LookaheadMode lookahead = SKIP_ALL, char ignore = NO_IGNORE_CHAR);

    size_t readBytes(char* buffer, size_t length);
    size_t readBytes(uint8_t* buffer, size_t length) { return readBytes(reinterpret_cast<char*>(buffer), length); }
    size_t readBytesUntil(char terminator, char* buffer, size_t length);
    size_t readBytesUntil(char terminator, uint8_t* buffer, size_t length)
    {
        return readBytesUntil(terminator, reinterpret_cast<char*>(buffer), length);
    }

    String readString();
    String readStringUntil(char terminator);

protected:
    long parseInt(char ignore) { return parseInt(SKIP_ALL, ignore); }
    float parseFloat(char ignore) { return parseFloat(SKIP_ALL, ignore); }

    struct MultiTarget {
        const char* str;
        size_t len;
        size_t index;
    };

    /* Returns the index of the first target found, or -1 on timeout. */
    int findMulti(MultiTarget* targets, int tCount);
};

#endif /* Stream_h */
