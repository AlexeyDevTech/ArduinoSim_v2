/*
 * avr/pgmspace.h - flash access compatibility. There is no separate flash on
 * the simulator: PROGMEM is empty and every pgm_read_* is a plain read.
 */
#ifndef __PGMSPACE_H_
#define __PGMSPACE_H_ 1

#include <stdint.h>
#include <stdio.h>
#include <string.h>
#ifdef _WIN32
#include <strings.h>
#endif

#define PROGMEM
#define PGM_P const char*
#define PGM_VOID_P const void*
#define PSTR(s) (s)

typedef void prog_void;
typedef char prog_char;
typedef unsigned char prog_uchar;
typedef int8_t prog_int8_t;
typedef uint8_t prog_uint8_t;
typedef int16_t prog_int16_t;
typedef uint16_t prog_uint16_t;
typedef int32_t prog_int32_t;
typedef uint32_t prog_uint32_t;

#define memchr_P memchr
#define memcmp_P memcmp
#define memccpy_P memccpy
#define memcpy_P memcpy
#define strcat_P strcat
#define strchr_P strchr
#define strcmp_P strcmp
#define strcpy_P strcpy
#define strcasecmp_P strcasecmp
#define strncasecmp_P strncasecmp
#define strcspn_P strcspn
#define strlen_P strlen
#define strnlen_P strnlen
#define strncat_P strncat
#define strncmp_P strncmp
#define strncpy_P strncpy
#define strpbrk_P strpbrk
#define strrchr_P strrchr
#define strspn_P strspn
#define strstr_P strstr
#define sprintf_P sprintf
#define snprintf_P snprintf
#define vsnprintf_P vsnprintf
#define vsprintf_P vsprintf
#define printf_P printf
#define fprintf_P fprintf
#define fputs_P fputs

#define pgm_read_byte(addr) (*(const unsigned char*)(addr))
#define pgm_read_float(addr) (*(const float*)(addr))
#define pgm_read_ptr(addr) (*(void* const*)(addr))

#ifdef __cplusplus
#include <type_traits>

/* On AVR a pointer is 16 bits, so sketches read PROGMEM tables of pointers with
 * pgm_read_word. Here pointers are 64 bits: reading through a pointer-typed
 * address returns the whole pointer so (char*)pgm_read_word(&table[i]) works. */
template <typename T>
inline auto sim_pgm_read_word(const T* address)
{
    if constexpr (std::is_pointer<typename std::remove_cv<T>::type>::value) {
        return reinterpret_cast<uintptr_t>(*address);
    } else {
        return *reinterpret_cast<const uint16_t*>(address);
    }
}
inline uint16_t sim_pgm_read_word(const void* address) { return *static_cast<const uint16_t*>(address); }

template <typename T>
inline auto sim_pgm_read_dword(const T* address)
{
    if constexpr (std::is_pointer<typename std::remove_cv<T>::type>::value) {
        return reinterpret_cast<uintptr_t>(*address);
    } else {
        return *reinterpret_cast<const uint32_t*>(address);
    }
}
inline uint32_t sim_pgm_read_dword(const void* address) { return *static_cast<const uint32_t*>(address); }

#define pgm_read_word(addr) sim_pgm_read_word(addr)
#define pgm_read_dword(addr) sim_pgm_read_dword(addr)
#else
#define pgm_read_word(addr) (*(const uint16_t*)(addr))
#define pgm_read_dword(addr) (*(const uint32_t*)(addr))
#endif

#define pgm_read_byte_near(addr) pgm_read_byte(addr)
#define pgm_read_word_near(addr) pgm_read_word(addr)
#define pgm_read_dword_near(addr) pgm_read_dword(addr)
#define pgm_read_float_near(addr) pgm_read_float(addr)
#define pgm_read_ptr_near(addr) pgm_read_ptr(addr)
#define pgm_read_byte_far(addr) pgm_read_byte(addr)
#define pgm_read_word_far(addr) pgm_read_word(addr)
#define pgm_read_dword_far(addr) pgm_read_dword(addr)
#define pgm_read_float_far(addr) pgm_read_float(addr)
#define pgm_read_ptr_far(addr) pgm_read_ptr(addr)
#define pgm_get_far_address(var) ((uintptr_t)(&(var)))

#endif /* __PGMSPACE_H_ */
