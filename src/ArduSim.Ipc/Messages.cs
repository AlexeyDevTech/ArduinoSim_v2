using MessagePack;

namespace ArduSim.Ipc;

/// <summary>Protocol constants (ADR-003). Bump <see cref="Version"/> on any incompatible change.</summary>
public static class IpcProtocol
{
    public const int Version = 2;
    public const int MaxFrameBytes = 16 << 20;
}

/// <summary>Every message on the pipe, in both directions. Union keys are part of the protocol.</summary>
[Union(0, typeof(Hello))]
[Union(1, typeof(Snapshot))]
[Union(2, typeof(FirmwareBusy))]
[Union(10, typeof(PauseRun))]
[Union(11, typeof(ResumeRun))]
[Union(12, typeof(StepRun))]
[Union(13, typeof(SetSpeed))]
[Union(14, typeof(ComponentInput))]
[Union(15, typeof(SerialInput))]
[Union(16, typeof(StopRun))]
[Union(17, typeof(SetRecording))]
public abstract record IpcMessage;

/// <summary>First message each way.</summary>
[MessagePackObject]
public sealed record Hello([property: Key(0)] int ProtocolVersion, [property: Key(1)] string ProductVersion) : IpcMessage;

public enum RunState : byte
{
    Running,
    Paused,
}

/// <param name="Kind">A <c>DriveKind</c> of the MCU model: 0 input, 1 LOW, 2 HIGH, 3 PWM, 4 tone.</param>
[MessagePackObject]
public sealed record PinUpdate([property: Key(0)] int Pin, [property: Key(1)] byte Kind, [property: Key(2)] int Value);

/// <param name="Display">A new picture of the part's display, when it has one and it changed.</param>
[MessagePackObject]
public sealed record ComponentUpdate(
    [property: Key(0)] string Id,
    [property: Key(1)] IReadOnlyDictionary<string, double> State,
    [property: Key(2)] DisplayData? Display = null);

/// <summary>A display's picture (ADR-006). Character LCD: <paramref name="Dots"/> is rows x 8 dot rows of columns 5-bit cells.</summary>
/// <param name="Kind">1 = character LCD.</param>
/// <param name="Status">Bit 0 powered, bit 1 display on, bit 2 backlight.</param>
/// <param name="Contrast">0 (nothing visible) to 1 (darkest).</param>
[MessagePackObject]
public sealed record DisplayData(
    [property: Key(0)] byte Kind,
    [property: Key(1)] int Columns,
    [property: Key(2)] int Rows,
    [property: Key(3)] byte Status,
    [property: Key(4)] float Contrast,
    [property: Key(5)] byte[] Dots);

/// <param name="Severity">0 info, 1 warning, 2 error.</param>
/// <param name="Message">English text; the UI shows its own translation of <paramref name="Key"/> with <paramref name="Args"/> when it has one.</param>
/// <param name="Key">Resource key of the message: the code, or <c>code.variant</c>.</param>
[MessagePackObject]
public sealed record LogEntry(
    [property: Key(0)] ulong TimeNs,
    [property: Key(1)] byte Severity,
    [property: Key(2)] string Code,
    [property: Key(3)] string Message,
    [property: Key(4)] string? ComponentId,
    [property: Key(5)] string? Key = null,
    [property: Key(6)] IReadOnlyList<string>? Args = null);

/// <summary>Serial output of a port other than the first.</summary>
[MessagePackObject]
public sealed record SerialOutput([property: Key(0)] int Port, [property: Key(1)] byte[] Data);

/// <summary>
/// What the logic analyzer recorded since the previous snapshot (ADR-014), as flat arrays: pin
/// samples in <paramref name="PinTimes"/>, two bytes each in <paramref name="PinData"/> (pin, level)
/// and <paramref name="PinValues"/>; bus events in <paramref name="BusTimes"/>,
/// <paramref name="BusDurations"/> and six bytes each in <paramref name="BusData"/> (kind, value, extra, three pins).
/// </summary>
/// <param name="Overflowed">Events were dropped since the last block.</param>
[MessagePackObject]
public sealed record TraceBlock(
    [property: Key(0)] ulong[] PinTimes,
    [property: Key(1)] byte[] PinData,
    [property: Key(2)] int[] PinValues,
    [property: Key(3)] ulong[] BusTimes,
    [property: Key(4)] uint[] BusDurations,
    [property: Key(5)] byte[] BusData,
    [property: Key(6)] bool Overflowed);

/// <summary>
/// Runner → UI, at most 60 per second: what changed since the previous snapshot. The first
/// snapshot of a run carries every pin and component.
/// </summary>
/// <param name="WallSeconds">Wall time since the run started, for the speed readout.</param>
[MessagePackObject]
public sealed record Snapshot(
    [property: Key(0)] ulong TimeNs,
    [property: Key(1)] double WallSeconds,
    [property: Key(2)] RunState State,
    [property: Key(3)] IReadOnlyList<PinUpdate> Pins,
    [property: Key(4)] IReadOnlyList<ComponentUpdate> Components,
    [property: Key(5)] byte[] Serial,
    [property: Key(6)] IReadOnlyList<LogEntry> Logs,
    [property: Key(7)] IReadOnlyList<SerialOutput>? OtherSerial = null,
    [property: Key(8)] TraceBlock? Trace = null) : IpcMessage;

/// <summary>Runner → UI: no Arduino API call for 2 s of wall time; the UI offers Stop.</summary>
[MessagePackObject]
public sealed record FirmwareBusy([property: Key(0)] ulong TimeNs) : IpcMessage;

[MessagePackObject]
public sealed record PauseRun : IpcMessage;

[MessagePackObject]
public sealed record ResumeRun : IpcMessage;

public enum StepKind : byte
{
    /// <summary>Run until the next return from loop().</summary>
    Loop,

    /// <summary>Run <see cref="StepRun.Nanoseconds"/> of virtual time.</summary>
    Time,
}

[MessagePackObject]
public sealed record StepRun([property: Key(0)] StepKind Kind, [property: Key(1)] ulong Nanoseconds) : IpcMessage;

/// <param name="Speed">Pacing factor; <see cref="double.PositiveInfinity"/> means as fast as possible.</param>
[MessagePackObject]
public sealed record SetSpeed([property: Key(0)] double Speed) : IpcMessage;

/// <summary>UI → Runner: a press, a knob turn, a switch flip (applied at the next scheduling point).</summary>
[MessagePackObject]
public sealed record ComponentInput([property: Key(0)] string Component, [property: Key(1)] string Name, [property: Key(2)] double Value) : IpcMessage;

[MessagePackObject]
public sealed record SerialInput([property: Key(0)] int Port, [property: Key(1)] byte[] Data) : IpcMessage;

[MessagePackObject]
public sealed record StopRun : IpcMessage;

/// <summary>UI → Runner: start or stop recording for the logic analyzer.</summary>
[MessagePackObject]
public sealed record SetRecording([property: Key(0)] bool Enabled) : IpcMessage;
