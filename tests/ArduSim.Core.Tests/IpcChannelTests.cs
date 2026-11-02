using ArduSim.Ipc;

namespace ArduSim.Core.Tests;

public sealed class IpcChannelTests
{
    [Fact]
    public async Task Messages_survive_framing_and_serialization()
    {
        var stream = new MemoryStream();
        await using var channel = new IpcChannel(stream);
        var snapshot = new Snapshot(
            123_456,
            1.5,
            RunState.Paused,
            [new PinUpdate(13, 2, 0)],
            [new ComponentUpdate("led1", new Dictionary<string, double> { ["brightness"] = 0.5 })],
            "hi"u8.ToArray(),
            [new LogEntry(7, 1, "floating-input", "D2 is floating", "btn1", "floating-input.analog", ["D2", "N1"])]);

        await channel.WriteAsync(new Hello(IpcProtocol.Version, "test"), TestContext.Current.CancellationToken);
        await channel.WriteAsync(snapshot, TestContext.Current.CancellationToken);
        await channel.WriteAsync(new StepRun(StepKind.Time, 42), TestContext.Current.CancellationToken);
        await channel.WriteAsync(new StopRun(), TestContext.Current.CancellationToken);
        stream.Position = 0;

        Assert.Equal(new Hello(IpcProtocol.Version, "test"), await channel.ReadAsync(TestContext.Current.CancellationToken));
        var read = Assert.IsType<Snapshot>(await channel.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal((123_456ul, RunState.Paused), (read.TimeNs, read.State));
        Assert.Equal(new PinUpdate(13, 2, 0), Assert.Single(read.Pins));
        Assert.Equal(0.5, Assert.Single(read.Components).State["brightness"]);
        Assert.Equal("hi"u8.ToArray(), read.Serial);
        LogEntry log = Assert.Single(read.Logs);
        Assert.Equal(("btn1", "floating-input.analog"), (log.ComponentId, log.Key));
        Assert.Equal(["D2", "N1"], log.Args!);
        Assert.Equal(new StepRun(StepKind.Time, 42), await channel.ReadAsync(TestContext.Current.CancellationToken));
        Assert.IsType<StopRun>(await channel.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Null(await channel.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Crash_block_keeps_the_last_twenty_calls_in_order()
    {
        using CrashBlock block = CrashBlock.CreatePrivate();
        for (int i = 0; i < 25; i++)
        {
            block.Record(HostCall.DigitalWrite, 13, i, (ulong)i);
        }

        IReadOnlyList<HostCallRecord> calls = block.LastCalls();

        Assert.Equal(CrashBlock.RingCapacity, calls.Count);
        Assert.Equal(Enumerable.Range(5, 20), calls.Select(c => c.B));
        Assert.Null(block.ReadCrash());
    }
}
