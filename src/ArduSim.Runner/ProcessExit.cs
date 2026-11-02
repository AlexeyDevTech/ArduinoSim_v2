using System.Runtime.InteropServices;

namespace ArduSim.Runner;

/// <summary>
/// Runner exit codes, and the one way a run ends. A run usually ends from inside a host callback
/// with native firmware frames on the stack, so the process terminates outright instead of
/// unwinding: firmware.dll's static destructors must not call back into a shutting-down runtime.
/// </summary>
internal static partial class ProcessExit
{
    public const int Success = 0;
    public const int BuildFailed = 1;
    public const int FirmwareError = 2;
    public const int FirmwareBusy = 4; // 3 is what abort() returns
    public const int Usage = 64;
    public const int ToolchainUnavailable = 69;
    public const int InternalError = 70;

    private static readonly List<Action> Flushers = [];

    public static void OnExit(Action flush)
    {
        lock (Flushers)
        {
            Flushers.Add(flush);
        }
    }

    public static void Terminate(int exitCode)
    {
        lock (Flushers)
        {
            foreach (Action flush in Flushers)
            {
                flush();
            }

            Console.Out.Flush();
            Console.Error.Flush();
            TerminateProcess(GetCurrentProcess(), (uint)exitCode);
        }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
}
