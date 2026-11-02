// Stream: timed reads, searching and number parsing as in the AVR core.
#include "Stream.h"

#include <Arduino.h>

namespace {

// Length of the longest prefix of s that is a suffix of (s[0..matched) + c).
size_t advance_match(const char* s, size_t matched, char c)
{
    for (size_t k = matched + 1; k > 0; k--) {
        if (s[k - 1] == c && memcmp(s, s + matched - (k - 1), k - 1) == 0) {
            return k;
        }
    }
    return 0;
}

bool is_digit(int c) { return c >= '0' && c <= '9'; }

}  // namespace

void Stream::setTimeout(unsigned long timeout) { _timeout = timeout; }

int Stream::timedRead()
{
    _startMillis = millis();
    do {
        int c = read();
        if (c >= 0) {
            return c;
        }
    } while (millis() - _startMillis < _timeout);
    return -1;
}

int Stream::timedPeek()
{
    _startMillis = millis();
    do {
        int c = peek();
        if (c >= 0) {
            return c;
        }
    } while (millis() - _startMillis < _timeout);
    return -1;
}

int Stream::peekNextDigit(LookaheadMode lookahead, bool detectDecimal)
{
    while (true) {
        int c = timedPeek();
        if (c < 0 || c == '-' || is_digit(c) || (detectDecimal && c == '.')) {
            return c;
        }
        if (lookahead == SKIP_NONE) {
            return -1;
        }
        if (lookahead == SKIP_WHITESPACE && !(c == ' ' || c == '\t' || c == '\r' || c == '\n')) {
            return -1;
        }
        read();
    }
}

bool Stream::find(const char* target) { return findUntil(target, strlen(target), nullptr, 0); }

bool Stream::find(const char* target, size_t length) { return findUntil(target, length, nullptr, 0); }

bool Stream::findUntil(const char* target, const char* terminator)
{
    return findUntil(target, strlen(target), terminator, terminator != nullptr ? strlen(terminator) : 0);
}

bool Stream::findUntil(const char* target, size_t targetLen, const char* terminator, size_t termLen)
{
    if (terminator == nullptr) {
        MultiTarget targets[1] = {{target, targetLen, 0}};
        return findMulti(targets, 1) == 0;
    }
    MultiTarget targets[2] = {{target, targetLen, 0}, {terminator, termLen, 0}};
    return findMulti(targets, 2) == 0;
}

int Stream::findMulti(MultiTarget* targets, int tCount)
{
    for (int i = 0; i < tCount; i++) {
        if (targets[i].len == 0) {
            return i;
        }
    }
    while (true) {
        int c = timedRead();
        if (c < 0) {
            return -1;
        }
        for (int i = 0; i < tCount; i++) {
            MultiTarget& t = targets[i];
            t.index = advance_match(t.str, t.index, static_cast<char>(c));
            if (t.index == t.len) {
                return i;
            }
        }
    }
}

long Stream::parseInt(LookaheadMode lookahead, char ignore)
{
    bool negative = false;
    long value = 0;
    int c = peekNextDigit(lookahead, false);
    if (c < 0) {
        return 0;
    }
    do {
        if (c == ignore) {
            // skipped, e.g. a thousands separator
        } else if (c == '-') {
            negative = true;
        } else if (is_digit(c)) {
            value = value * 10 + c - '0';
        }
        read();
        c = timedPeek();
    } while (is_digit(c) || c == ignore);
    return negative ? -value : value;
}

float Stream::parseFloat(LookaheadMode lookahead, char ignore)
{
    bool negative = false;
    bool fraction = false;
    double value = 0.0;
    double scale = 1.0;
    int c = peekNextDigit(lookahead, true);
    if (c < 0) {
        return 0;
    }
    do {
        if (c == ignore) {
            // skipped
        } else if (c == '-') {
            negative = true;
        } else if (c == '.') {
            fraction = true;
        } else if (is_digit(c)) {
            if (fraction) {
                scale *= 0.1;
                value += scale * (c - '0');
            } else {
                value = value * 10 + (c - '0');
            }
        }
        read();
        c = timedPeek();
    } while (is_digit(c) || (c == '.' && !fraction) || c == ignore);
    return static_cast<float>(negative ? -value : value);
}

size_t Stream::readBytes(char* buffer, size_t length)
{
    size_t count = 0;
    while (count < length) {
        int c = timedRead();
        if (c < 0) {
            break;
        }
        *buffer++ = static_cast<char>(c);
        count++;
    }
    return count;
}

size_t Stream::readBytesUntil(char terminator, char* buffer, size_t length)
{
    size_t index = 0;
    while (index < length) {
        int c = timedRead();
        if (c < 0 || c == terminator) {
            break;
        }
        *buffer++ = static_cast<char>(c);
        index++;
    }
    return index;
}

String Stream::readString()
{
    String ret;
    int c = timedRead();
    while (c >= 0) {
        ret += static_cast<char>(c);
        c = timedRead();
    }
    return ret;
}

String Stream::readStringUntil(char terminator)
{
    String ret;
    int c = timedRead();
    while (c >= 0 && c != terminator) {
        ret += static_cast<char>(c);
        c = timedRead();
    }
    return ret;
}
