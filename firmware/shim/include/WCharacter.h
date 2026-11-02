/* WCharacter.h - Arduino character classification helpers. */
#ifndef Character_h
#define Character_h

#include <ctype.h>

inline bool isAlphaNumeric(int c) { return isalnum(c) != 0; }
inline bool isAlpha(int c) { return isalpha(c) != 0; }
inline bool isAscii(int c) { return (c & ~0x7f) == 0; }
inline bool isWhitespace(int c) { return c == ' ' || c == '\t'; }
inline bool isControl(int c) { return iscntrl(c) != 0; }
inline bool isDigit(int c) { return isdigit(c) != 0; }
inline bool isGraph(int c) { return isgraph(c) != 0; }
inline bool isLowerCase(int c) { return islower(c) != 0; }
inline bool isPrintable(int c) { return isprint(c) != 0; }
inline bool isPunct(int c) { return ispunct(c) != 0; }
inline bool isSpace(int c) { return isspace(c) != 0; }
inline bool isUpperCase(int c) { return isupper(c) != 0; }
inline bool isHexadecimalDigit(int c) { return isxdigit(c) != 0; }
inline int toAscii(int c) { return c & 0x7f; }
inline int toLowerCase(int c) { return tolower(c); }
inline int toUpperCase(int c) { return toupper(c); }

#endif /* Character_h */
