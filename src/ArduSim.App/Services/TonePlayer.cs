using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ArduSim.App.Services;

/// <summary>Plays the tones of the running buzzers.</summary>
public interface ITonePlayer : IDisposable
{
    /// <summary>The frequencies to sound now, in Hz; empty for silence.</summary>
    void SetTones(IReadOnlyList<double> frequencies);
}

/// <summary>No sound: for tests and offscreen runs.</summary>
public sealed class SilentTonePlayer : ITonePlayer
{
    public void SetTones(IReadOnlyList<double> frequencies)
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Square waves through the Windows wave output (winmm, no dependency): the device opens at the first
/// tone and closes after a second of silence. A tone shorter than a snapshot (about 16 ms) can be missed.
/// </summary>
public sealed partial class WaveOutTonePlayer : ITonePlayer
{
    private const int SampleRate = 44_100;
    private const int BufferFrames = 1024;
    private const int BufferCount = 3;
    private const short Amplitude = 6000;
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint HeaderDone = 1;
    private const int SilenceLimitMs = 1000;

    private readonly ILogger<WaveOutTonePlayer> _logger;
    private readonly Lock _gate = new();
    private double[] _tones = [];
    private Thread? _thread;
    private bool _disposed;
    private bool _failed;

    public WaveOutTonePlayer(ILogger<WaveOutTonePlayer> logger) => _logger = logger;

    public void SetTones(IReadOnlyList<double> frequencies)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        lock (_gate)
        {
            _tones = [.. frequencies];
            if (_tones.Length > 0 && _thread is null && !_disposed && !_failed)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "ArduSim tones" };
                _thread.Start();
            }
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            _disposed = true;
            thread = _thread;
        }

        thread?.Join(500);
    }

    [LibraryImport("winmm.dll")]
    private static partial int waveOutOpen(out nint device, uint deviceId, in WaveFormat format, nint callback, nint instance, uint flags);

    [LibraryImport("winmm.dll")]
    private static partial int waveOutPrepareHeader(nint device, nint header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial int waveOutUnprepareHeader(nint device, nint header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial int waveOutWrite(nint device, nint header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial int waveOutReset(nint device);

    [LibraryImport("winmm.dll")]
    private static partial int waveOutClose(nint device);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No sound: waveOutOpen returned {Code}.")]
    private static partial void LogOpenFailed(ILogger logger, int code);

    private void Run()
    {
        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = 1,
            SamplesPerSec = SampleRate,
            AvgBytesPerSec = SampleRate * 2,
            BlockAlign = 2,
            BitsPerSample = 16,
            Size = 0,
        };
        int code = waveOutOpen(out nint device, WaveMapper, format, 0, 0, 0);
        if (code != 0)
        {
            LogOpenFailed(_logger, code);
            lock (_gate)
            {
                _failed = true;
                _thread = null;
            }

            return;
        }

        uint headerSize = (uint)Marshal.SizeOf<WaveHeader>();
        var headers = new nint[BufferCount];
        var phases = new Dictionary<double, double>();
        var samples = new short[BufferFrames];
        for (int i = 0; i < BufferCount; i++)
        {
            nint data = Marshal.AllocHGlobal(BufferFrames * sizeof(short));
            headers[i] = Marshal.AllocHGlobal((int)headerSize);
            Marshal.StructureToPtr(new WaveHeader { Data = data, BufferLength = BufferFrames * sizeof(short), Flags = 0 }, headers[i], false);
            _ = waveOutPrepareHeader(device, headers[i], headerSize);
            Fill(samples, [], phases);
            Marshal.Copy(samples, 0, data, BufferFrames);
            _ = waveOutWrite(device, headers[i], headerSize);
        }

        int silentMs = 0;
        while (true)
        {
            double[] tones;
            lock (_gate)
            {
                tones = _tones;
                if (_disposed || (silentMs >= SilenceLimitMs && tones.Length == 0))
                {
                    _thread = null;
                    break;
                }
            }

            silentMs = tones.Length == 0 ? silentMs + 2 : 0;
            foreach (nint header in headers)
            {
                WaveHeader value = Marshal.PtrToStructure<WaveHeader>(header);
                if ((value.Flags & HeaderDone) != 0)
                {
                    Fill(samples, tones, phases);
                    Marshal.Copy(samples, 0, value.Data, BufferFrames);
                    _ = waveOutWrite(device, header, headerSize);
                }
            }

            Thread.Sleep(2);
        }

        _ = waveOutReset(device);
        foreach (nint header in headers)
        {
            WaveHeader value = Marshal.PtrToStructure<WaveHeader>(header);
            _ = waveOutUnprepareHeader(device, header, headerSize);
            Marshal.FreeHGlobal(value.Data);
            Marshal.FreeHGlobal(header);
        }

        _ = waveOutClose(device);
    }

    // Square waves, mixed; each keeps its phase across buffers so a held tone does not click.
    private static void Fill(short[] samples, double[] tones, Dictionary<double, double> phases)
    {
        Array.Clear(samples);
        foreach (double frequency in tones)
        {
            double phase = phases.GetValueOrDefault(frequency);
            double step = frequency / SampleRate;
            short level = (short)(Amplitude / tones.Length);
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] += phase < 0.5 ? level : (short)-level;
                phase += step;
                if (phase >= 1)
                {
                    phase -= 1;
                }
            }

            phases[frequency] = phase;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nint Reserved;
    }
}
