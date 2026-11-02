using ArduSim.Core.Diagnostics;

namespace ArduSim.Core.Tests;

public sealed class SimulationLogTests
{
    [Fact]
    public void Report_keeps_the_english_text_and_the_arguments_for_translation()
    {
        var log = new SimulationLog(() => 5);
        double volts = 2.345;

        log.Report(SimSeverity.Warning, "undefined-level", $"D2 is at {volts:0.0#} V on {"N3"}.", "btn1", "test");

        SimDiagnostic entry = Assert.Single(log.Entries);
        Assert.Equal("D2 is at 2.35 V on N3.", entry.Message);
        Assert.Equal(["2.35", "N3"], entry.Args);
        Assert.Equal("undefined-level.test", entry.Key);
    }

    [Fact]
    public void Key_is_the_code_without_a_variant()
    {
        var log = new SimulationLog(() => 0);

        Assert.True(log.ReportOnce("k", SimSeverity.Info, "firmware", $"{"hello"}"));
        Assert.False(log.ReportOnce("k", SimSeverity.Info, "firmware", $"{"again"}"));

        Assert.Equal("firmware", Assert.Single(log.Entries).Key);
    }
}
