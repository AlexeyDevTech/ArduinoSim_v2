# U8g2 compat

`src/clib/u8x8_8x8.c` replaces the vendored file of the same path (U8g2 tag 2.37.1) at build time.
Only `u8x8_pgm_read_esp()` differs: with `ESP8266` defined, U8g2 reads font bytes through a
32-bit aligned load whose address goes through `(uint32_t)addr`, which cuts a 64-bit pointer and
crashes the simulated board. The copy uses `uintptr_t` for the same arithmetic. Everything else
in the file is unchanged; drop this copy when U8g2 is updated, and re-check.