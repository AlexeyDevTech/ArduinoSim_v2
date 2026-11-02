using System.Runtime.InteropServices;
using ArduSim.Ipc;

namespace ArduSim.Runner.Interop;

/// <summary>
/// The functions firmware.dll calls. Each one forwards to the process's single
/// <see cref="SimulationHost"/>; an exception must not cross back into native code, so every
/// callback turns one into an internal-error report and ends the run.
/// </summary>
internal static unsafe class HostCallbacks
{
    private static SimulationHost _host = null!;

    public static SimHostApi* Create(SimulationHost host, SimCrashInfo* crashInfo)
    {
        _host = host;
        var api = (SimHostApi*)NativeMemory.AllocZeroed((nuint)sizeof(SimHostApi));
        api->StructSize = (uint)sizeof(SimHostApi);
        api->Version = SimHostApi.AbiVersion;
        api->PinMode = &PinMode;
        api->DigitalWrite = &DigitalWrite;
        api->DigitalRead = &DigitalRead;
        api->AnalogRead = &AnalogRead;
        api->AnalogWrite = &AnalogWrite;
        api->AnalogReference = &AnalogReference;
        api->Tone = &Tone;
        api->NoTone = &NoTone;
        api->NowUs = &NowUs;
        api->DelayUs = &DelayUs;
        api->Yield = &Yield;
        api->SerialBegin = &SerialBegin;
        api->SerialEnd = &SerialEnd;
        api->SerialWrite = &SerialWrite;
        api->SerialRead = &SerialRead;
        api->SerialPeek = &SerialPeek;
        api->SerialAvailable = &SerialAvailable;
        api->SerialAvailableForWrite = &SerialAvailableForWrite;
        api->SerialFlush = &SerialFlush;
        api->I2cBegin = &I2cBegin;
        api->I2cEnd = &I2cEnd;
        api->I2cSetClock = &I2cSetClock;
        api->I2cStart = &I2cStart;
        api->I2cWrite = &I2cWrite;
        api->I2cRead = &I2cRead;
        api->I2cStop = &I2cStop;
        api->SpiBegin = &SpiBegin;
        api->SpiEnd = &SpiEnd;
        api->SpiSettings = &SpiSettings;
        api->SpiTransfer = &SpiTransfer;
        api->EepromRead = &EepromRead;
        api->EepromWrite = &EepromWrite;
        api->AttachInterrupt = &AttachInterrupt;
        api->DetachInterrupt = &DetachInterrupt;
        api->SetInterruptsEnabled = &SetInterruptsEnabled;
        api->Log = &Log;
        api->Fatal = &Fatal;
        api->CrashInfo = crashInfo;
        return api;
    }

    [UnmanagedCallersOnly]
    private static void PinMode(int pin, int mode)
    {
        try { _host.Enter(HostCall.PinMode, pin, mode).PinMode(pin, mode); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void DigitalWrite(int pin, int level)
    {
        try { _host.Enter(HostCall.DigitalWrite, pin, level).DigitalWrite(pin, level); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static int DigitalRead(int pin)
    {
        try { return _host.Enter(HostCall.DigitalRead, pin).DigitalRead(pin); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int AnalogRead(int pin)
    {
        try { return _host.Enter(HostCall.AnalogRead, pin).AnalogRead(pin); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static void AnalogWrite(int pin, int value)
    {
        try { _host.Enter(HostCall.AnalogWrite, pin, value).AnalogWrite(pin, value); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void AnalogReference(int mode)
    {
        try { _host.Enter(HostCall.AnalogReference, mode).AnalogReferenceMode(mode); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void Tone(int pin, uint frequency, uint durationMs)
    {
        try { _host.Enter(HostCall.Tone, pin, (int)frequency).Tone(pin, frequency, durationMs); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void NoTone(int pin)
    {
        try { _host.Enter(HostCall.NoTone, pin).NoTone(pin); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static ulong NowUs()
    {
        try { return _host.Enter(HostCall.Micros).NowMicros(); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static void DelayUs(uint us)
    {
        try { _host.Enter(HostCall.DelayMicroseconds, (int)us).DelayMicros(us); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void Yield()
    {
        try { _host.Enter(HostCall.Yield).Yield(); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void SerialBegin(int port, uint baud, uint config)
    {
        try { _host.Enter(HostCall.SerialBegin, port, (int)baud).SerialBegin(port, baud, config); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void SerialEnd(int port)
    {
        try { _host.Enter(HostCall.SerialEnd, port).SerialEnd(port); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static int SerialWrite(int port, byte* data, int length)
    {
        try { return _host.Enter(HostCall.SerialWrite, port, length).SerialWrite(port, new ReadOnlySpan<byte>(data, Math.Max(length, 0))); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int SerialRead(int port)
    {
        try { return _host.Enter(HostCall.SerialRead, port).SerialRead(port); }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int SerialPeek(int port)
    {
        try { return _host.Enter(HostCall.SerialPeek, port).SerialPeek(port); }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int SerialAvailable(int port)
    {
        try { return _host.Enter(HostCall.SerialAvailable, port).SerialAvailable(port); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int SerialAvailableForWrite(int port)
    {
        try { return _host.Enter(HostCall.SerialAvailableForWrite, port).SerialAvailableForWrite(port); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static void SerialFlush(int port)
    {
        try { _host.Enter(HostCall.SerialFlush, port).SerialFlush(port); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void I2cBegin()
    {
        try { _host.Enter(HostCall.I2c).I2cBegin(); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void I2cEnd()
    {
        try { _host.Enter(HostCall.I2c).I2cEnd(); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void I2cSetClock(uint hz)
    {
        try { _host.Enter(HostCall.I2c, (int)Math.Min(hz, int.MaxValue)).I2cSetClock(hz); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static int I2cStart(byte address, int read)
    {
        try { return _host.Enter(HostCall.I2c, address, read).I2cStart(address, read != 0) ? 1 : 0; }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int I2cWrite(byte value)
    {
        try { return _host.Enter(HostCall.I2c, value).I2cWrite(value) ? 1 : 0; }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static byte I2cRead(int ack)
    {
        try { return _host.Enter(HostCall.I2c).I2cRead(ack != 0); }
        catch (Exception e) { _host.InternalError(e); return 0xFF; }
    }

    [UnmanagedCallersOnly]
    private static void I2cStop()
    {
        try { _host.Enter(HostCall.I2c).I2cStop(); }
        catch (Exception e) { _host.InternalError(e); }
    }

    // SPI arrives with the OLED milestone (M5): until then it warns and sees no device.
    [UnmanagedCallersOnly]
    private static void SpiBegin() => NotSimulated("SPI");

    [UnmanagedCallersOnly]
    private static void SpiEnd() => NotSimulated("SPI");

    [UnmanagedCallersOnly]
    private static void SpiSettings(uint clock, int bitOrder, int mode) => NotSimulated("SPI");

    [UnmanagedCallersOnly]
    private static byte SpiTransfer(byte value)
    {
        NotSimulated("SPI");
        return 0xFF;
    }

    [UnmanagedCallersOnly]
    private static byte EepromRead(int address)
    {
        try { return _host.Enter(HostCall.EepromRead, address).EepromRead(address); }
        catch (Exception e) { _host.InternalError(e); return 0xFF; }
    }

    [UnmanagedCallersOnly]
    private static void EepromWrite(int address, byte value)
    {
        try { _host.Enter(HostCall.EepromWrite, address, value).EepromWrite(address, value); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void AttachInterrupt(int number, int mode)
    {
        try { _host.Enter(HostCall.AttachInterrupt, number, mode).AttachInterrupt(number, mode); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void DetachInterrupt(int number)
    {
        try { _host.Enter(HostCall.DetachInterrupt, number).DetachInterrupt(number); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void SetInterruptsEnabled(int enabled)
    {
        try { _host.Enter(HostCall.SetInterruptsEnabled, enabled).SetInterruptsEnabled(enabled != 0); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void Log(int level, byte* message)
    {
        try { _host.FirmwareLog(level, Marshal.PtrToStringUTF8((nint)message) ?? ""); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void Fatal(byte* message)
    {
        try { _host.FirmwareFatal(Marshal.PtrToStringUTF8((nint)message) ?? ""); }
        catch (Exception e) { _host.InternalError(e); }
    }

    private static void NotSimulated(string bus)
    {
        try { _host.Enter(HostCall.Spi).BusNotSimulated(bus); }
        catch (Exception e) { _host.InternalError(e); }
    }
}
