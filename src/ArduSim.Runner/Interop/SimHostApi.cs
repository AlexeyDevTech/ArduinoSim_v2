using System.Runtime.InteropServices;

namespace ArduSim.Runner.Interop;

/// <summary>
/// Mirror of <c>SimHostApi</c> in firmware/shim/include/sim_abi.h. Field order and types must match
/// the header exactly; the firmware rejects a table smaller than its own (<see cref="StructSize"/>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SimHostApi
{
    public const uint AbiMajor = 1;
    public const uint AbiMinor = 0;
    public const uint AbiVersion = (AbiMajor << 16) | AbiMinor;

    public uint StructSize;
    public uint Version;

    public delegate* unmanaged<int, int, void> PinMode;
    public delegate* unmanaged<int, int, void> DigitalWrite;
    public delegate* unmanaged<int, int> DigitalRead;
    public delegate* unmanaged<int, int> AnalogRead;
    public delegate* unmanaged<int, int, void> AnalogWrite;
    public delegate* unmanaged<int, void> AnalogReference;
    public delegate* unmanaged<int, uint, uint, void> Tone;
    public delegate* unmanaged<int, void> NoTone;

    public delegate* unmanaged<ulong> NowUs;
    public delegate* unmanaged<uint, void> DelayUs;
    public delegate* unmanaged<void> Yield;

    public delegate* unmanaged<int, uint, uint, void> SerialBegin;
    public delegate* unmanaged<int, void> SerialEnd;
    public delegate* unmanaged<int, byte*, int, int> SerialWrite;
    public delegate* unmanaged<int, int> SerialRead;
    public delegate* unmanaged<int, int> SerialPeek;
    public delegate* unmanaged<int, int> SerialAvailable;
    public delegate* unmanaged<int, int> SerialAvailableForWrite;
    public delegate* unmanaged<int, void> SerialFlush;

    public delegate* unmanaged<void> I2cBegin;
    public delegate* unmanaged<void> I2cEnd;
    public delegate* unmanaged<uint, void> I2cSetClock;
    public delegate* unmanaged<byte, int, int> I2cStart;
    public delegate* unmanaged<byte, int> I2cWrite;
    public delegate* unmanaged<int, byte> I2cRead;
    public delegate* unmanaged<void> I2cStop;

    public delegate* unmanaged<void> SpiBegin;
    public delegate* unmanaged<void> SpiEnd;
    public delegate* unmanaged<uint, int, int, void> SpiSettings;
    public delegate* unmanaged<byte, byte> SpiTransfer;

    public delegate* unmanaged<int, byte> EepromRead;
    public delegate* unmanaged<int, byte, void> EepromWrite;

    public delegate* unmanaged<int, int, void> AttachInterrupt;
    public delegate* unmanaged<int, void> DetachInterrupt;
    public delegate* unmanaged<int, void> SetInterruptsEnabled;

    public delegate* unmanaged<int, byte*, void> Log;
    public delegate* unmanaged<byte*, void> Fatal;
    public SimCrashInfo* CrashInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SimCrashInfo
{
    public uint Magic;
    public uint ExceptionCode;
    public ulong ExceptionAddress;
    public ulong FirmwareBase;
    public ulong AccessAddress;
}
