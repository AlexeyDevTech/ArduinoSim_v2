using System.Runtime.InteropServices;
using ArduSim.Core.Boards;
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
        api->PwmConfig = &PwmConfig;
        api->DacWrite = &DacWrite;
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
        api->KvGet = &KvGet;
        api->KvPut = &KvPut;
        api->KvRemove = &KvRemove;
        api->KvClear = &KvClear;
        api->Restart = &Restart;
        api->WifiScanStart = &WifiScanStart;
        api->WifiScanCount = &WifiScanCount;
        api->WifiScanEntry = &WifiScanEntry;
        api->WifiBegin = &WifiBegin;
        api->WifiStatus = &WifiStatus;
        api->WifiDisconnect = &WifiDisconnect;
        api->WifiSoftAp = &WifiSoftAp;
        api->WifiAddress = &WifiAddress;
        api->WifiRssi = &WifiRssi;
        api->NetConnect = &NetConnect;
        api->NetState = &NetState;
        api->NetAvailable = &NetAvailable;
        api->NetRead = &NetRead;
        api->NetWrite = &NetWrite;
        api->NetClose = &NetClose;
        api->NetListen = &NetListen;
        api->NetAccept = &NetAccept;
        api->NetRemote = &NetRemote;
        api->NetResolve = &NetResolve;
        api->NetResolveResult = &NetResolveResult;
        api->UdpOpen = &UdpOpen;
        api->UdpSend = &UdpSend;
        api->UdpReceive = &UdpReceive;
        api->ResetReason = &ResetReason;
        api->DeepSleep = &DeepSleep;
        api->TimerStart = &TimerStart;
        api->TimerStop = &TimerStop;
        api->LedStripWrite = &LedStripWrite;
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
    private static void PwmConfig(int pin, uint frequency, int bits)
    {
        try { _host.Enter(HostCall.PwmConfig, pin, bits).PwmConfig(pin, frequency, bits); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void DacWrite(int pin, int value)
    {
        try { _host.Enter(HostCall.DacWrite, pin, value).DacWrite(pin, value); }
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
    private static void SerialBegin(int port, uint baud, uint config, int rx, int tx)
    {
        try { _host.Enter(HostCall.SerialBegin, port, (int)baud).SerialBegin(port, baud, config, rx, tx); }
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
    private static void I2cBegin(int bus, int sda, int scl)
    {
        try { _host.Enter(HostCall.I2c, bus, sda).I2cBegin(bus, sda, scl); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void I2cEnd(int bus)
    {
        try { _host.Enter(HostCall.I2c, bus).I2cEnd(bus); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void I2cSetClock(int bus, uint hz)
    {
        try { _host.Enter(HostCall.I2c, bus, (int)Math.Min(hz, int.MaxValue)).I2cSetClock(bus, hz); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static int I2cStart(int bus, byte address, int read)
    {
        try { return _host.Enter(HostCall.I2c, address, read).I2cStart(bus, address, read != 0) ? 1 : 0; }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int I2cWrite(int bus, byte value)
    {
        try { return _host.Enter(HostCall.I2c, value).I2cWrite(bus, value) ? 1 : 0; }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static byte I2cRead(int bus, int ack)
    {
        try { return _host.Enter(HostCall.I2c).I2cRead(bus, ack != 0); }
        catch (Exception e) { _host.InternalError(e); return 0xFF; }
    }

    [UnmanagedCallersOnly]
    private static void I2cStop(int bus)
    {
        try { _host.Enter(HostCall.I2c).I2cStop(bus); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void SpiBegin(int bus, int sck, int miso, int mosi, int ss)
    {
        try { _host.Enter(HostCall.Spi, bus, sck).SpiBegin(bus, new SpiPins(mosi, miso, sck, ss)); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void SpiEnd(int bus)
    {
        try { _host.Enter(HostCall.Spi, bus).SpiEnd(bus); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void SpiSettings(int bus, uint clock, int bitOrder, int mode)
    {
        try { _host.Enter(HostCall.Spi, (int)Math.Min(clock, int.MaxValue), mode).SpiSettings(bus, clock, bitOrder, mode); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static byte SpiTransfer(int bus, byte value)
    {
        try { return _host.Enter(HostCall.Spi, value).SpiTransfer(bus, value); }
        catch (Exception e) { _host.InternalError(e); return 0xFF; }
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
    private static int KvGet(byte* nameSpace, byte* key, int* type, byte* buffer, int capacity)
    {
        try
        {
            int length = _host.Enter(HostCall.Preferences).KvGet(Text(nameSpace), Text(key), out int stored, new Span<byte>(buffer, Math.Max(capacity, 0)));
            *type = stored;
            return length;
        }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int KvPut(byte* nameSpace, byte* key, int type, byte* data, int length)
    {
        try { return _host.Enter(HostCall.Preferences, type, length).KvPut(Text(nameSpace), Text(key), type, new ReadOnlySpan<byte>(data, Math.Max(length, 0))); }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int KvRemove(byte* nameSpace, byte* key)
    {
        try { return _host.Enter(HostCall.Preferences).KvRemove(Text(nameSpace), Text(key)) ? 1 : 0; }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static void KvClear(byte* nameSpace)
    {
        try { _host.Enter(HostCall.Preferences).KvClear(Text(nameSpace)); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void Restart()
    {
        try
        {
            _host.Enter(HostCall.Restart);
            _host.Restart();
        }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void WifiScanStart()
    {
        try { _host.Enter(HostCall.WiFi).WifiScanStart(); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static int WifiScanCount()
    {
        try { return _host.Enter(HostCall.WiFi).WifiScanCount(); }
        catch (Exception e) { _host.InternalError(e); return -2; }
    }

    [UnmanagedCallersOnly]
    private static int WifiScanEntry(int index, byte* ssid, int capacity, int* rssi, int* channel, int* open)
    {
        try
        {
            if (_host.Enter(HostCall.WiFi, index).WifiScanEntry(index) is not { } network)
            {
                return -1;
            }

            CopyText(network.Ssid, ssid, capacity);
            *rssi = network.Rssi;
            *channel = network.Channel;
            *open = network.Password.Length == 0 ? 1 : 0;
            return 0;
        }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static void WifiBegin(byte* ssid, byte* password)
    {
        try { _host.Enter(HostCall.WiFi).WifiBegin(Text(ssid), Text(password)); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static int WifiStatus()
    {
        try { return _host.Enter(HostCall.WiFi).WifiStatusCode(); }
        catch (Exception e) { _host.InternalError(e); return 6; }
    }

    [UnmanagedCallersOnly]
    private static void WifiDisconnect()
    {
        try { _host.Enter(HostCall.WiFi).WifiDisconnect(); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static int WifiSoftAp(byte* ssid, byte* password)
    {
        try { return _host.Enter(HostCall.WiFi).WifiSoftAp(Text(ssid), Text(password)) ? 1 : 0; }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static uint WifiAddress(int which)
    {
        try { return _host.Enter(HostCall.WiFi, which).WifiAddress(which); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int WifiRssi()
    {
        try { return _host.Enter(HostCall.WiFi).WifiRssi(); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int NetConnect(byte* host, ushort port, int tls, byte* caPem)
    {
        try { return _host.EnterNetwork(port, tls).Connect(Text(host), port, tls, caPem is null ? null : Text(caPem)); }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int NetState(int handle)
    {
        try { return _host.EnterNetwork(handle).State(handle); }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int NetAvailable(int handle)
    {
        try { return _host.EnterNetwork(handle).Available(handle); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int NetRead(int handle, byte* buffer, int capacity)
    {
        try { return _host.EnterNetwork(handle, capacity).Read(handle, new Span<byte>(buffer, Math.Max(capacity, 0))); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int NetWrite(int handle, byte* data, int length)
    {
        try { return _host.EnterNetwork(handle, length).Write(handle, new ReadOnlySpan<byte>(data, Math.Max(length, 0))); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static void NetClose(int handle)
    {
        try { _host.EnterNetwork(handle).Close(handle); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static int NetListen(ushort port)
    {
        try { return _host.EnterNetwork(port).Listen(port); }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int NetAccept(int listener)
    {
        try { return _host.EnterNetwork(listener).Accept(listener); }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int NetRemote(int handle, uint* ip, ushort* port)
    {
        try
        {
            bool known = _host.EnterNetwork(handle).Remote(handle, out uint address, out ushort remotePort);
            *ip = address;
            *port = remotePort;
            return known ? 1 : 0;
        }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int NetResolve(byte* host)
    {
        try { return _host.EnterNetwork().Resolve(Text(host)); }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int NetResolveResult(int handle, uint* ip)
    {
        try
        {
            int result = _host.EnterNetwork(handle).ResolveResult(handle, out uint address);
            *ip = address;
            return result;
        }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int UdpOpen(ushort port)
    {
        try { return _host.EnterNetwork(port).UdpOpen(port); }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int UdpSend(int handle, byte* host, ushort port, byte* data, int length)
    {
        try { return _host.EnterNetwork(handle, length).UdpSend(handle, Text(host), port, new ReadOnlySpan<byte>(data, Math.Max(length, 0))) ? 1 : 0; }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static int UdpReceive(int handle, byte* buffer, int capacity, uint* ip, ushort* port)
    {
        try
        {
            int length = _host.EnterNetwork(handle).UdpReceive(handle, new Span<byte>(buffer, Math.Max(capacity, 0)), out uint address, out ushort remotePort);
            *ip = address;
            *port = remotePort;
            return length;
        }
        catch (Exception e) { _host.InternalError(e); return -1; }
    }

    [UnmanagedCallersOnly]
    private static int ResetReason()
    {
        try { return (int)_host.Enter(HostCall.Restart).ResetReason; }
        catch (Exception e) { _host.InternalError(e); return 0; }
    }

    [UnmanagedCallersOnly]
    private static void DeepSleep(ulong microseconds)
    {
        try
        {
            _host.Enter(HostCall.Restart, (int)Math.Min(microseconds / 1000, int.MaxValue));
            _host.DeepSleep(microseconds);
        }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void TimerStart(int timer, ulong periodNanoseconds)
    {
        try { _host.Enter(HostCall.Timer, timer, (int)Math.Min(periodNanoseconds, int.MaxValue)).StartTimer(timer, periodNanoseconds); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void TimerStop(int timer)
    {
        try { _host.Enter(HostCall.Timer, timer, -1).StopTimer(timer); }
        catch (Exception e) { _host.InternalError(e); }
    }

    [UnmanagedCallersOnly]
    private static void LedStripWrite(int pin, byte* data, int length, int khz)
    {
        try { _host.Enter(HostCall.LedStrip, pin, length).LedStripWrite(pin, new ReadOnlySpan<byte>(data, Math.Max(length, 0)), khz); }
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

    private static string Text(byte* utf8) => Marshal.PtrToStringUTF8((nint)utf8) ?? "";

    private static void CopyText(string text, byte* buffer, int capacity)
    {
        if (capacity <= 0)
        {
            return;
        }

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        int length = Math.Min(bytes.Length, capacity - 1);
        bytes.AsSpan(0, length).CopyTo(new Span<byte>(buffer, length));
        buffer[length] = 0;
    }
}
