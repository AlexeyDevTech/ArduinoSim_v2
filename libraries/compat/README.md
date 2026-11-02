# Per-library compatibility shims

One folder per library that has no portable (non-AVR) code path. A shim is layered on top of the vendored source by the library resolver; each one documents what it replaces and why.
