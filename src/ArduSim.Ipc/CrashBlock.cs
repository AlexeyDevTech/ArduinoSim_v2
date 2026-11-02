using System.Globalization;
using System.IO.MemoryMappedFiles;

namespace ArduSim.Ipc;

/// <summary>Host calls recorded in the crash ring.</summary>
public enum HostCall : uint
{
    None = 0,
    PinMode,
    DigitalWrite,
    DigitalRead,
    AnalogRead,
    AnalogWrite,
    AnalogReference,
    Tone,
    NoTone,
    Micros,
    DelayMicroseconds,
    Yield,
    SerialBegin,
    SerialEnd,
    SerialWrite,
    SerialRead,
    SerialPeek,
    SerialAvailable,
    SerialAvailableForWrite,
    SerialFlush,
    I2c,
    Spi,
    EepromRead,
    EepromWrite,
    AttachInterrupt,
    DetachInterrupt,
    SetInterruptsEnabled,
    Log,
    Fatal,
}

/// <param name="TimeNs">Virtual time of the call.</param>
public sealed record HostCallRecord(HostCall Call, int A, int B, ulong TimeNs)
{
    public override string ToString()
    {
        string name = Call switch
        {
            HostCall.Micros => "micros/millis",
            HostCall.DelayMicroseconds => "delay",
            HostCall.SetInterruptsEnabled => "interrupts",
            _ => char.ToLowerInvariant(Call.ToString()[0]) + Call.ToString()[1..],
        };
        string args = Call switch
        {
            HostCall.PinMode or HostCall.DigitalWrite or HostCall.AnalogWrite or HostCall.Tone or HostCall.EepromWrite or HostCall.AttachInterrupt or HostCall.SerialBegin
                => string.Create(CultureInfo.InvariantCulture, $"{A}, {B}"),
            HostCall.DigitalRead or HostCall.AnalogRead or HostCall.NoTone or HostCall.EepromRead or HostCall.DetachInterrupt or HostCall.DelayMicroseconds or HostCall.SetInterruptsEnabled or HostCall.AnalogReference
                => A.ToString(CultureInfo.InvariantCulture),
            HostCall.SerialWrite => string.Create(CultureInfo.InvariantCulture, $"{B} bytes"),
            _ => "",
        };
        return string.Create(CultureInfo.InvariantCulture, $"{TimeNs / 1e6,12:F3} ms  {name}({args})");
    }
}

/// <summary>What the firmware's exception handler recorded (ADR-003).</summary>
/// <param name="AccessKind">For access violations: 0 read, 1 write, 8 execute.</param>
/// <param name="ReturnAddress">First return address into firmware.dll found on the faulting stack (0 if none).</param>
public sealed record CrashDetails(
    uint ExceptionCode,
    ulong ExceptionAddress,
    ulong FirmwareBase,
    ulong AccessAddress,
    uint AccessKind,
    ulong ReturnAddress,
    IReadOnlyList<HostCallRecord> LastCalls)
{
    public string ExceptionName => ExceptionCode switch
    {
        0xC0000005 => "access violation",
        0xC0000094 => "integer division by zero",
        0xC00000FD => "stack overflow",
        0xC000001D => "illegal instruction",
        0xC0000095 => "integer overflow",
        0xC0000096 => "privileged instruction",
        0xC000008C => "array bounds exceeded",
        0x80000002 => "misaligned data access",
        _ => string.Create(CultureInfo.InvariantCulture, $"exception 0x{ExceptionCode:X8}"),
    };
}

/// <summary>
/// Shared memory between ArduSim.App and one Runner (ADR-003). The firmware's vectored exception
/// handler writes <c>SimCrashInfo</c> at offset 0 (layout in sim_abi.h); the Runner writes a ring of
/// the last host calls from offset 64. Both survive the Runner's death, so the App can explain a crash.
/// </summary>
public sealed unsafe class CrashBlock : IDisposable
{
    public const int Size = 4096;
    public const int RingCapacity = 20;
    private const uint Magic = 0x48535253;
    private const int RingHeader = 64;
    private const int RingEntries = RingHeader + 16;
    private const int EntrySize = 24;

    private readonly MemoryMappedFile? _file;
    private readonly MemoryMappedViewAccessor? _view;
    private readonly byte* _memory;
    private readonly bool _ownsNative;

    private CrashBlock(MemoryMappedFile? file, MemoryMappedViewAccessor? view, byte* memory, bool ownsNative)
    {
        _file = file;
        _view = view;
        _memory = memory;
        _ownsNative = ownsNative;
        *(uint*)(_memory + RingHeader) = RingCapacity;
    }

    /// <summary>Start of the block, handed to the firmware as <c>SimHostApi.crash_info</c>.</summary>
    public nint Address => (nint)_memory;

    /// <summary>Creates a named block (the App side, before starting the Runner).</summary>
    public static CrashBlock CreateNamed(string name)
    {
        MemoryMappedFile file = MemoryMappedFile.CreateNew(name, Size);
        return FromFile(file);
    }

    /// <summary>Opens a block the App created (the Runner side).</summary>
#pragma warning disable CA1416 // ArduSim is Windows-only (brief §2); named shared memory is a Windows feature
    public static CrashBlock OpenNamed(string name) => FromFile(MemoryMappedFile.OpenExisting(name));
#pragma warning restore CA1416

    /// <summary>A private block for headless runs.</summary>
    public static CrashBlock CreatePrivate()
    {
        byte* memory = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(Size);
        return new CrashBlock(null, null, memory, ownsNative: true);
    }

    public void Record(HostCall call, int a, int b, ulong timeNs)
    {
        ulong count = *(ulong*)(_memory + RingHeader + 8);
        byte* entry = _memory + RingEntries + ((int)(count % RingCapacity) * EntrySize);
        *(uint*)entry = (uint)call;
        *(int*)(entry + 4) = a;
        *(int*)(entry + 8) = b;
        *(ulong*)(entry + 16) = timeNs;
        *(ulong*)(_memory + RingHeader + 8) = count + 1;
    }

    /// <summary>The firmware's crash record, or null when the firmware did not crash.</summary>
    public CrashDetails? ReadCrash()
    {
        if (*(uint*)_memory != Magic)
        {
            return null;
        }

        return new CrashDetails(
            *(uint*)(_memory + 4),
            *(ulong*)(_memory + 8),
            *(ulong*)(_memory + 16),
            *(ulong*)(_memory + 24),
            *(uint*)(_memory + 40),
            *(ulong*)(_memory + 32),
            LastCalls());
    }

    public IReadOnlyList<HostCallRecord> LastCalls()
    {
        ulong count = *(ulong*)(_memory + RingHeader + 8);
        int available = (int)Math.Min(count, RingCapacity);
        var calls = new List<HostCallRecord>(available);
        for (ulong i = count - (ulong)available; i < count; i++)
        {
            byte* entry = _memory + RingEntries + ((int)(i % RingCapacity) * EntrySize);
            calls.Add(new HostCallRecord((HostCall)(*(uint*)entry), *(int*)(entry + 4), *(int*)(entry + 8), *(ulong*)(entry + 16)));
        }

        return calls;
    }

    public void Dispose()
    {
        if (_view is not null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
            _file!.Dispose();
        }
        else if (_ownsNative)
        {
            System.Runtime.InteropServices.NativeMemory.Free(_memory);
        }
    }

    private static CrashBlock FromFile(MemoryMappedFile file)
    {
        MemoryMappedViewAccessor view = file.CreateViewAccessor(0, Size);
        byte* memory = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref memory);
        return new CrashBlock(file, view, memory + view.PointerOffset, ownsNative: false);
    }
}
