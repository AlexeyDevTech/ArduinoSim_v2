using System.Diagnostics;
using System.Text;

namespace ArduSim.Build.Processes;

internal sealed record ProcessResult(int ExitCode, string Output);

internal static class ProcessRunner
{
    // Variables that would make the toolchain pick up headers or libraries from another installation.
    private static readonly string[] ScrubbedVariables = ["CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "LIBRARY_PATH", "INCLUDE", "LIB"];

    /// <summary>Runs a tool and returns its exit code with stdout and stderr merged in arrival order.</summary>
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string name in ScrubbedVariables)
        {
            start.Environment.Remove(name);
        }

        var output = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        lock (output)
        {
            return new ProcessResult(process.ExitCode, output.ToString());
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.Append(line).Append('\n');
        }
    }
}
