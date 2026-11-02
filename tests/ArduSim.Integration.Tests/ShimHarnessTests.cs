using System.Diagnostics;

namespace ArduSim.Integration.Tests;

/// <summary>Compiles the C++ shim standalone with its fake-host harness (firmware/shim/test) and runs it.</summary>
public sealed class ShimHarnessTests(ToolchainFixture toolchain)
{
    [Fact]
    public async Task Shim_compiles_warning_free_and_passes_its_harness()
    {
        string shim = Path.Combine(RunnerProcess.RepositoryRoot, "firmware", "shim");
        string output = Directory.CreateTempSubdirectory("ardusim-shim-").FullName;
        string exe = Path.Combine(output, "shim_tests.exe");
        var compile = new ProcessStartInfo(toolchain.Toolchain.CxxCompiler)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        string[] flags =
        [
            "-std=gnu++17", "-fno-exceptions", "-fno-rtti", "-Wall", "-Wextra", "-Werror", "-O1", "-g", "-fno-color-diagnostics",
            "-DARDUINO=10819", "-DARDUINO_ARCH_SIM", "-DSIM_BOARD_NANO", "-DF_CPU=16000000L",
            "-I" + Path.Combine(shim, "include"), "-I" + Path.Combine(shim, "variants", "nano"), "-I" + Path.Combine(shim, "libraries", "EEPROM", "src"),
            "-I" + Path.Combine(shim, "libraries", "Wire", "src"),
            .. Directory.EnumerateFiles(Path.Combine(shim, "src"), "*.cpp"),
            Path.Combine(shim, "libraries", "Wire", "src", "Wire.cpp"),
            Path.Combine(shim, "test", "shim_tests.cpp"),
            "-static", "-o", exe,
        ];
        foreach (string flag in flags)
        {
            compile.ArgumentList.Add(flag);
        }

        (int compileExit, string compileOutput) = await Run(compile);
        Assert.True(compileExit == 0, compileOutput);

        (int exit, string text) = await Run(new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true });
        Assert.True(exit == 0, text);
        Assert.Contains(" 0 failed", text, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> Run(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await stdout + await stderr);
    }
}
