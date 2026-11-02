// Toolchain smoke loader: loads a smoke firmware.dll, wires the draft
// SimHostApi to managed [UnmanagedCallersOnly] callbacks, runs setup/loop,
// measures host-call throughput and checks that dbghelp maps an export back
// to a source line (needed for crash diagnostics).
//
// Usage: dotnet run SmokeLoader.cs -- <path\to\smoke.dll>
#:property AllowUnsafeBlocks=true
#:property Nullable=enable

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run SmokeLoader.cs -- <firmware.dll>");
    return 2;
}

return Smoke.Run(Path.GetFullPath(args[0]));

[StructLayout(LayoutKind.Sequential)]
unsafe struct SimHostApi
{
    public uint StructSize;
    public uint AbiVersion;
    public delegate* unmanaged<int, int, void> PinMode;
    public delegate* unmanaged<int, int, void> DigitalWrite;
    public nint DigitalRead, AnalogRead, AnalogWrite, AnalogReference, Tone, NoTone;
    public nint NowUs, DelayUs, Yield;
    public nint SerialBegin, SerialEnd;
    public delegate* unmanaged<int, byte*, int, int> SerialWrite;
    public nint SerialRead, SerialPeek, SerialAvailable, SerialAvailableForWrite, SerialFlush;
    public nint I2cBegin, I2cEnd, I2cSetClock, I2cStart, I2cWrite, I2cRead, I2cStop;
    public nint SpiBegin, SpiEnd, SpiSettings, SpiTransfer;
    public nint EepromRead, EepromWrite;
    public nint AttachInterrupt, DetachInterrupt, SetInterruptsEnabled;
    public delegate* unmanaged<int, byte*, void> Log;
    public nint Fatal;
    public nint CrashInfo;
}

static unsafe class Host
{
    public static long DigitalWrites;
    public static int PinModeCalls;
    public static readonly StringBuilder SerialOut = new();

    [UnmanagedCallersOnly]
    public static void PinMode(int pin, int mode) => PinModeCalls++;

    [UnmanagedCallersOnly]
    public static void DigitalWrite(int pin, int level) => DigitalWrites++;

    [UnmanagedCallersOnly]
    public static int SerialWrite(int port, byte* data, int length)
    {
        SerialOut.Append(Encoding.ASCII.GetString(data, length));
        return length;
    }

    [UnmanagedCallersOnly]
    public static void Log(int level, byte* message) =>
        Console.WriteLine($"  [fw log {level}] {Marshal.PtrToStringUTF8((nint)message)}");
}

static unsafe class Smoke
{
    public static int Run(string dllPath)
    {
        var failures = new List<string>();

        var api = (SimHostApi*)NativeMemory.AllocZeroed((nuint)sizeof(SimHostApi));
        api->StructSize = (uint)sizeof(SimHostApi);
        api->AbiVersion = 1u << 16;
        api->PinMode = &Host.PinMode;
        api->DigitalWrite = &Host.DigitalWrite;
        api->SerialWrite = &Host.SerialWrite;
        api->Log = &Host.Log;
        Environment.SetEnvironmentVariable("ARDUSIM_HOST_API", ((nint)api).ToString("x"));

        var sw = Stopwatch.StartNew();
        nint lib = NativeLibrary.Load(dllPath);
        Console.WriteLine($"  load: {sw.Elapsed.TotalMilliseconds:F1} ms, struct_size={sizeof(SimHostApi)}");
        if (Host.PinModeCalls != 1) failures.Add($"static constructor did not reach the host (pin_mode calls = {Host.PinModeCalls})");

        var abiVersion = (delegate* unmanaged<uint>)NativeLibrary.GetExport(lib, "sim_fw_abi_version");
        var init = (delegate* unmanaged<SimHostApi*, int>)NativeLibrary.GetExport(lib, "sim_fw_init");
        var setup = (delegate* unmanaged<void>)NativeLibrary.GetExport(lib, "sim_fw_setup");
        nint loopAddress = NativeLibrary.GetExport(lib, "sim_fw_loop");
        var loop = (delegate* unmanaged<void>)loopAddress;
        _ = NativeLibrary.GetExport(lib, "sim_fw_dispatch_interrupt");

        if (abiVersion() != (1u << 16)) failures.Add($"abi version 0x{abiVersion():x}");
        if (init(api) != 0) failures.Add("sim_fw_init rejected the host");
        setup();
        if (Host.SerialOut.ToString() != "smoke setup 3.14\n") failures.Add($"serial output was '{Host.SerialOut}'");

        for (int i = 0; i < 200; i++) loop(); // warm-up
        Host.DigitalWrites = 0;
        sw.Restart();
        const int Loops = 5000;
        for (int i = 0; i < Loops; i++) loop();
        double seconds = sw.Elapsed.TotalSeconds;
        if (Host.DigitalWrites != Loops * 1000L) failures.Add($"digital_write calls = {Host.DigitalWrites}");
        Console.WriteLine($"  host calls: {Host.DigitalWrites / seconds / 1e6:F1} M/s ({seconds * 1e9 / Host.DigitalWrites:F1} ns per call)");

        string where = Symbolize(dllPath, lib, loopAddress);
        Console.WriteLine($"  dbghelp: sim_fw_loop -> {where}");
        if (!where.Contains("smoke_firmware.cpp:", StringComparison.OrdinalIgnoreCase)) failures.Add("dbghelp could not resolve a source line");

        foreach (string f in failures) Console.WriteLine($"  FAIL: {f}");
        Console.WriteLine(failures.Count == 0 ? "  SMOKE OK" : "  SMOKE FAILED");
        return failures.Count == 0 ? 0 : 1;
    }

    static string Symbolize(string dllPath, nint moduleBase, nint address)
    {
        nint process = Process.GetCurrentProcess().Handle;
        SymSetOptions(0x2 | 0x10); // UNDNAME | LOAD_LINES; DEFERRED_LOADS makes the line lookup fail with 126
        if (!SymInitializeW(process, Path.GetDirectoryName(dllPath), false)) return $"SymInitialize failed ({Marshal.GetLastPInvokeError()})";
        try
        {
            if (SymLoadModuleExW(process, 0, dllPath, null, (ulong)moduleBase, 0, 0, 0) == 0 && Marshal.GetLastPInvokeError() != 0)
                return $"SymLoadModuleEx failed ({Marshal.GetLastPInvokeError()})";
            var line = new ImagehlpLineW64 { SizeOfStruct = (uint)sizeof(ImagehlpLineW64) };
            if (!SymGetLineFromAddrW64(process, (ulong)address, out _, ref line))
                return $"SymGetLineFromAddr failed ({Marshal.GetLastPInvokeError()})";
            return $"{Marshal.PtrToStringUni(line.FileName)}:{line.LineNumber}";
        }
        finally
        {
            SymCleanup(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ImagehlpLineW64
    {
        public uint SizeOfStruct;
        public nint Key;
        public uint LineNumber;
        public nint FileName;
        public ulong Address;
    }

    [DllImport("dbghelp.dll")]
    static extern uint SymSetOptions(uint options);

    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SymInitializeW(nint process, string? searchPath, bool invadeProcess);

    [DllImport("dbghelp.dll")]
    static extern bool SymCleanup(nint process);

    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ulong SymLoadModuleExW(nint process, nint file, string imageName, string? moduleName, ulong baseOfDll, uint dllSize, nint data, uint flags);

    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SymGetLineFromAddrW64(nint process, ulong address, out uint displacement, ref ImagehlpLineW64 line);
}
