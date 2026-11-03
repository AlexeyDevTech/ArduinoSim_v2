# SdFat compat

Two files replace the vendored files of the same paths (SdFat 2.3.1) at build time:

- `src/SdFatConfig.h`: a copy that sets `USE_FCNTL_H 0` under the simulator, so every board uses
  SdFat's own `O_*` open flags. On ESP32 SdFat takes them from `<fcntl.h>`, and the host's has no
  `O_NONBLOCK`.
- `src/iostream/ostream.h`: a copy whose `operator<<(const void*)` goes through `uintptr_t`; the
  original casts a pointer straight to `uint32_t`, which does not compile on the 64-bit host.

The Arduino `SD` library is not bundled (GPL-3.0); the shim's own `SD` library (firmware/shim/libraries/SD)
implements its API on SdFat.