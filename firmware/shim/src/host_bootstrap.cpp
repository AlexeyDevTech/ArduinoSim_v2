// Locates the simulator's SimHostApi and records crashes. The only shim file that includes <windows.h>.
#undef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00 // Windows 10: GetCurrentThreadStackLimits
#include "sim/host.h"

#include <stdio.h>
#include <stdlib.h>

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

namespace sim {

const SimHostApi* g_host = nullptr;

bool is_compatible(const SimHostApi* api)
{
    return api != nullptr && (api->abi_version >> 16) == SIM_ABI_VERSION_MAJOR &&
           (api->abi_version & 0xFFFFu) >= SIM_ABI_VERSION_MINOR && api->struct_size >= sizeof(SimHostApi);
}

// Called on the first host call when it happens before sim_fw_init, i.e. from a
// C++ static constructor while ArduSim.Runner is still inside LoadLibrary.
const SimHostApi* resolve_host()
{
    char text[32];
    DWORD n = GetEnvironmentVariableA(SIM_HOST_API_ENV, text, sizeof text);
    if (n == 0 || n >= sizeof text) {
        fprintf(stderr, "ArduSim firmware: no simulator host (%s is not set). Run the firmware through ArduSim.Runner.\n", SIM_HOST_API_ENV);
        abort();
    }

    const SimHostApi* api = reinterpret_cast<const SimHostApi*>(static_cast<uintptr_t>(strtoull(text, nullptr, 16)));
    if (!is_compatible(api)) {
        fprintf(stderr, "ArduSim firmware: the simulator host is incompatible with firmware ABI %u.%u. Rebuild the sketch.\n",
                static_cast<unsigned>(SIM_ABI_VERSION_MAJOR), static_cast<unsigned>(SIM_ABI_VERSION_MINOR));
        abort();
    }

    g_host = api;
    return api;
}

namespace {

struct ThreadStart {
    void (*entry)(void*);
    void* argument;
};

DWORD WINAPI thread_main(LPVOID parameter)
{
    ThreadStart start = *static_cast<ThreadStart*>(parameter);
    delete static_cast<ThreadStart*>(parameter);
    start.entry(start.argument);
    return 0;
}

}  // namespace

void* signal_create() { return CreateEventW(nullptr, FALSE, FALSE, nullptr); }

void signal_set(void* signal) { SetEvent(static_cast<HANDLE>(signal)); }

void signal_wait(void* signal) { WaitForSingleObject(static_cast<HANDLE>(signal), INFINITE); }

bool thread_start(void (*entry)(void*), void* argument, size_t stack_bytes)
{
    // x64 code needs far more stack than the task's ESP32 size; this only reserves it.
    HANDLE thread = CreateThread(nullptr, stack_bytes, thread_main, new ThreadStart{entry, argument}, STACK_SIZE_PARAM_IS_A_RESERVATION, nullptr);
    if (thread == nullptr) {
        return false;
    }

    CloseHandle(thread);
    return true;
}

namespace {

constexpr uint32_t CrashMagic = 0x48535253;

SimCrashInfo* g_crash = nullptr;
uintptr_t g_firmware_begin = 0;
uintptr_t g_firmware_end = 0;

bool in_firmware(uintptr_t address) { return address >= g_firmware_begin && address < g_firmware_end; }

bool is_fatal(DWORD code)
{
    switch (code) {
    case EXCEPTION_ACCESS_VIOLATION:
    case EXCEPTION_INT_DIVIDE_BY_ZERO:
    case EXCEPTION_INT_OVERFLOW:
    case EXCEPTION_STACK_OVERFLOW:
    case EXCEPTION_ILLEGAL_INSTRUCTION:
    case EXCEPTION_PRIV_INSTRUCTION:
    case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:
    case EXCEPTION_DATATYPE_MISALIGNMENT:
        return true;
    default:
        return false;
    }
}

// The C runtime the sketch calls into (strcpy on a null pointer faults inside ucrtbase).
bool in_c_runtime(uintptr_t address)
{
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            reinterpret_cast<LPCWSTR>(address), &module)) {
        return false;  // JIT-compiled managed code has no module: not ours
    }
    return module == GetModuleHandleW(L"ucrtbase.dll") || module == GetModuleHandleW(L"msvcrt.dll");
}

// Scans the faulting stack for the first return address into firmware.dll, so a crash inside the
// C runtime still points at the sketch line that called it.
uint64_t find_firmware_return(const CONTEXT* context)
{
    ULONG_PTR low = 0;
    ULONG_PTR high = 0;
    GetCurrentThreadStackLimits(&low, &high);
    const uint64_t* slot = reinterpret_cast<const uint64_t*>(context->Rsp);
    for (int i = 0; i < 2048 && reinterpret_cast<ULONG_PTR>(slot + 1) <= high; i++, slot++) {
        if (in_firmware(static_cast<uintptr_t>(*slot))) {
            return *slot;
        }
    }
    return 0;
}

void append_hex(char*& out, uint64_t value)
{
    *out++ = '0';
    *out++ = 'x';
    bool started = false;
    for (int shift = 60; shift >= 0; shift -= 4) {
        unsigned digit = static_cast<unsigned>((value >> shift) & 0xF);
        if (digit != 0 || started || shift == 0) {
            started = true;
            *out++ = static_cast<char>(digit < 10 ? '0' + digit : 'A' + digit - 10);
        }
    }
}

void append_text(char*& out, const char* text)
{
    while (*text != '\0') {
        *out++ = *text++;
    }
}

// The record is in shared memory; end the process now rather than wait for the .NET runtime and
// Windows Error Reporting to finish with the fault (seconds). One line on stderr for headless runs.
[[noreturn]] void report_and_terminate(DWORD code, uintptr_t address)
{
    char line[160];
    char* out = line;
    append_text(out, "ArduSim firmware crashed: exception ");
    append_hex(out, code);
    append_text(out, " at firmware.dll+");
    append_hex(out, address - g_firmware_begin);
    append_text(out, "\n");
    DWORD written = 0;
    WriteFile(GetStdHandle(STD_ERROR_HANDLE), line, static_cast<DWORD>(out - line), &written, nullptr);
    TerminateProcess(GetCurrentProcess(), code);
    for (;;) {
    }
}

LONG CALLBACK on_exception(EXCEPTION_POINTERS* info)
{
    const EXCEPTION_RECORD* record = info->ExceptionRecord;
    if (g_crash == nullptr || g_crash->magic == CrashMagic || !is_fatal(record->ExceptionCode)) {
        return EXCEPTION_CONTINUE_SEARCH;
    }

    uintptr_t address = reinterpret_cast<uintptr_t>(record->ExceptionAddress);
    bool overflow = record->ExceptionCode == EXCEPTION_STACK_OVERFLOW;  // almost no stack left: do the minimum
    if (!in_firmware(address) && (overflow || !in_c_runtime(address))) {
        return EXCEPTION_CONTINUE_SEARCH;  // the .NET runtime handles its own faults
    }

    g_crash->exception_status = record->ExceptionCode;
    g_crash->exception_address = address;
    if (record->ExceptionCode == EXCEPTION_ACCESS_VIOLATION && record->NumberParameters >= 2) {
        g_crash->access_kind = static_cast<uint32_t>(record->ExceptionInformation[0]);
        g_crash->access_address = record->ExceptionInformation[1];
    }
    g_crash->return_address = overflow ? 0 : find_firmware_return(info->ContextRecord);
    MemoryBarrier();
    g_crash->magic = CrashMagic;
    report_and_terminate(record->ExceptionCode, address);
    return EXCEPTION_CONTINUE_SEARCH;
}

}  // namespace

void install_crash_handler(SimCrashInfo* crash)
{
    if (crash == nullptr || g_crash != nullptr) {
        return;
    }

    HMODULE self = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCWSTR>(&on_exception), &self);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(self);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(reinterpret_cast<const char*>(self) + dos->e_lfanew);
    g_firmware_begin = reinterpret_cast<uintptr_t>(self);
    g_firmware_end = g_firmware_begin + nt->OptionalHeader.SizeOfImage;
    crash->firmware_base = g_firmware_begin;
    g_crash = crash;
    AddVectoredExceptionHandler(1, on_exception);
}

}  // namespace sim
