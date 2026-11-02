# ADR-002: Portable C++ toolchain

- Status: Accepted — llvm-mingw (confirmed by the owner on 2026-11-02).
- Date: 2026-11-02

## Context

The app compiles sketches itself (brief §3.4). The toolchain is downloaded on first run into `%LOCALAPPDATA%\ArduSim\toolchain\<version>` with progress and checksum verification, without admin rights, and must produce an x64 DLL from gnu++17 code that runs on a clean Windows 10/11. Settings may point at an existing clang, MSVC or MinGW instead.

Candidates:

1. **zig cc** — one archive with clang, lld, libc++ and mingw-w64 CRT *sources*; runtime libraries are compiled on first use and cached.
2. **llvm-mingw** (mstorsjo) — prebuilt clang + lld + libc++ + mingw-w64 for the UCRT, the standard LLVM driver.
3. **Official LLVM for Windows** — rejected without measurement of builds: 640 MB (MSI) or 276–901 MB (tarballs) for 23.1.4, and it targets the MSVC ABI, so linking needs the Visual Studio libraries and Windows SDK, which a clean machine does not have.

## Measurements

Method: `tools/toolchain-eval/Measure-Toolchains.ps1` downloads each archive, verifies SHA-256, extracts it with the in-box `tar.exe`, compiles `tools/toolchain-eval/smoke/smoke_firmware.cpp` with our flags against the draft `sim_abi.h`, links a DLL, lists its imports, then `SmokeLoader.cs` loads the DLL from .NET 10, calls every export through the draft ABI (including a static constructor that reaches the host before `sim_fw_init`), measures host-call throughput and asks the in-box `dbghelp.dll` for the source line of `sim_fw_loop`.

Machine: Windows 11 Pro 26200, x64. PATH reduced to `System32` during compilation, private zig caches. This approximates, but is not, a clean VM (see Risks).

| | zig 0.17.0 | llvm-mingw 20261020 (UCRT, x86_64) |
| --- | --- | --- |
| Archive (zip) | **95.6 MiB** | 181.8 MiB |
| Unpacked | **361 MiB** | 715 MiB (~450 MiB without the arm/i686 sysroots, Python and LLDB, which we do not extract) |
| Compile of the smoke TU | 0.75–0.84 s | **0.43–0.49 s** |
| Link, warm | 0.47–0.49 s | **0.13–0.15 s** |
| Link, very first | 31 s (builds libc++, libunwind, CRT from source) | **0.2 s** |
| DLL imports | `KERNEL32` + UCRT api-sets | `KERNEL32` + UCRT api-sets |
| PDB, dbghelp resolves `file:line` | yes | yes (`-gcodeview -Wl,--pdb=`) |
| Host call through the ABI | ~5 ns | ~5 ns |
| gnu++17, `-fno-exceptions -fno-rtti`, admin rights | ok, none | ok, none |

Both DLLs depend only on components present on every Windows 10/11 (UCRT ships with the OS since Windows 10).

## Decision

Use **llvm-mingw 20261020, UCRT, x86_64**, pinned by URL and SHA-256 (`fd5462b7f5d93b2ff981fcd3f9c7e427e47f3203c64ea91c27bcc444eebd74cd`, 190 612 575 bytes) in a toolchain manifest inside `ArduSim.Build`.

- Compile: `x86_64-w64-mingw32-clang++ -std=gnu++17 -fno-exceptions -fno-rtti -Wall -O1 -g -gcodeview -c` (C files with `clang -std=gnu11`).
- Link: `-shared -static -g -gcodeview -Wl,--pdb=` → `firmware.dll` + `firmware.pdb`, no runtime DLLs to ship.
- Crash diagnostics symbolize with the in-box `dbghelp.dll` (verified); `llvm-symbolizer` ships with the toolchain as a second option.
- Extraction skips `aarch64-*`, `armv7-*`, `i686-*`, `python` and LLDB.

Why not zig: it halves the download, but compiles are ~1.8× and links ~3× slower — directly against the "warm rebuild under 3 s" budget, where a rebuild is one sketch TU plus a link — and the first build costs an extra ~30 s. zig's `cc` is also a translation layer over clang flags, whereas llvm-mingw is the plain clang driver, so compiler output and flags behave as documented. zig stays the documented fallback if download size ever matters more.

## Consequences

- One-time download of 182 MiB; progress UI and resumable download are worth it.
- The object cache keys include the toolchain version, so upgrading the pin invalidates it cleanly.
- Settings can still point at an existing clang/MSVC/MinGW; those paths get the same smoke check before use.

## Risks

- Not yet verified on a truly clean Windows 10 VM. The M1 bootstrap runs the same smoke test after every download, so a broken toolchain is reported with a "re-download" action rather than failing later.
