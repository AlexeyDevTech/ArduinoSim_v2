using System.Runtime.InteropServices;

namespace ArduSim.Runner.Interop;

/// <summary>
/// Mirror of <c>SimHostApi</c> in firmware/shim/include/sim_abi.h. Field order and types must match
/// the header exactly; the firmware rejects a table smaller than its own (<see cref="StructSize"/>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SimHostApi
{
    public const uint AbiMajor = 2;
    public const uint AbiMinor = 1;
    public const uint AbiVersion = (AbiMajor << 16) | AbiMinor;

    public uint StructSize;
    public uint Version;

    public delegate* unmanaged<int, int, void> PinMode;
    public delegate* unmanaged<int, int, void> DigitalWrite;
    public delegate* unmanaged<int, int> DigitalRead;
    public delegate* unmanaged<int, int> AnalogRead;
    public delegate* unmanaged<int, int, void> AnalogWrite;
    public delegate* unmanaged<int, uint, int, void> PwmConfig;
    public delegate* unmanaged<int, int, void> DacWrite;
    public delegate* unmanaged<int, void> AnalogReference;
    public delegate* unmanaged<int, uint, uint, void> Tone;
    public delegate* unmanaged<int, void> NoTone;

    public delegate* unmanaged<ulong> NowUs;
    public delegate* unmanaged<uint, void> DelayUs;
    public delegate* unmanaged<void> Yield;

    public delegate* unmanaged<int, uint, uint, int, int, void> SerialBegin;
    public delegate* unmanaged<int, void> SerialEnd;
    public delegate* unmanaged<int, byte*, int, int> SerialWrite;
    public delegate* unmanaged<int, int> SerialRead;
    public delegate* unmanaged<int, int> SerialPeek;
    public delegate* unmanaged<int, int> SerialAvailable;
    public delegate* unmanaged<int, int> SerialAvailableForWrite;
    public delegate* unmanaged<int, void> SerialFlush;

    public delegate* unmanaged<int, int, int, void> I2cBegin;
    public delegate* unmanaged<int, void> I2cEnd;
    public delegate* unmanaged<int, uint, void> I2cSetClock;
    public delegate* unmanaged<int, byte, int, int> I2cStart;
    public delegate* unmanaged<int, byte, int> I2cWrite;
    public delegate* unmanaged<int, int, byte> I2cRead;
    public delegate* unmanaged<int, void> I2cStop;

    public delegate* unmanaged<int, int, int, int, int, void> SpiBegin;
    public delegate* unmanaged<int, void> SpiEnd;
    public delegate* unmanaged<int, uint, int, int, void> SpiSettings;
    public delegate* unmanaged<int, byte, byte> SpiTransfer;

    public delegate* unmanaged<int, byte> EepromRead;
    public delegate* unmanaged<int, byte, void> EepromWrite;

    public delegate* unmanaged<int, int, void> AttachInterrupt;
    public delegate* unmanaged<int, void> DetachInterrupt;
    public delegate* unmanaged<int, void> SetInterruptsEnabled;

    public delegate* unmanaged<int, byte*, void> Log;
    public delegate* unmanaged<byte*, void> Fatal;
    public SimCrashInfo* CrashInfo;

    public delegate* unmanaged<byte*, byte*, int*, byte*, int, int> KvGet;
    public delegate* unmanaged<byte*, byte*, int, byte*, int, int> KvPut;
    public delegate* unmanaged<byte*, byte*, int> KvRemove;
    public delegate* unmanaged<byte*, void> KvClear;

    public delegate* unmanaged<void> Restart;

    public delegate* unmanaged<void> WifiScanStart;
    public delegate* unmanaged<int> WifiScanCount;
    public delegate* unmanaged<int, byte*, int, int*, int*, int*, int> WifiScanEntry;
    public delegate* unmanaged<byte*, byte*, void> WifiBegin;
    public delegate* unmanaged<int> WifiStatus;
    public delegate* unmanaged<void> WifiDisconnect;
    public delegate* unmanaged<byte*, byte*, int> WifiSoftAp;
    public delegate* unmanaged<int, uint> WifiAddress;
    public delegate* unmanaged<int> WifiRssi;

    public delegate* unmanaged<byte*, ushort, int, byte*, int> NetConnect;
    public delegate* unmanaged<int, int> NetState;
    public delegate* unmanaged<int, int> NetAvailable;
    public delegate* unmanaged<int, byte*, int, int> NetRead;
    public delegate* unmanaged<int, byte*, int, int> NetWrite;
    public delegate* unmanaged<int, void> NetClose;
    public delegate* unmanaged<ushort, int> NetListen;
    public delegate* unmanaged<int, int> NetAccept;
    public delegate* unmanaged<int, uint*, ushort*, int> NetRemote;
    public delegate* unmanaged<byte*, int> NetResolve;
    public delegate* unmanaged<int, uint*, int> NetResolveResult;
    public delegate* unmanaged<ushort, int> UdpOpen;
    public delegate* unmanaged<int, byte*, ushort, byte*, int, int> UdpSend;
    public delegate* unmanaged<int, byte*, int, uint*, ushort*, int> UdpReceive;

    public delegate* unmanaged<int> ResetReason;
    public delegate* unmanaged<ulong, void> DeepSleep;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SimCrashInfo
{
    public uint Magic;
    public uint ExceptionCode;
    public ulong ExceptionAddress;
    public ulong FirmwareBase;
    public ulong AccessAddress;
    public ulong ReturnAddress;
    public uint AccessKind;
    public uint Reserved;
}
