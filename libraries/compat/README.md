# Per-library compatibility shims

One folder per library that has no portable (non-AVR) code path. A file at `compat/<library folder>/<path>` replaces the vendored source with the same relative path when the firmware is built (the vendored file stays untouched, its folder stays on the include path); each library folder has a README saying what is replaced and why.

- `U8g2`: `src/clib/u8x8_8x8.c`, a 64-bit-safe ESP8266 font read.
