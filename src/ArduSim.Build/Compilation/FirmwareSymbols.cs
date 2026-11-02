using System.Runtime.InteropServices;

namespace ArduSim.Build.Compilation;

/// <param name="Function">Undecorated function name, when known.</param>
/// <param name="File">Source file, when the PDB has line information for the address.</param>
public sealed record SourceLocation(string? Function, string? File, int Line)
{
    public override string ToString() =>
        File is null ? Function ?? "?" : $"{Function ?? "?"} at {Path.GetFileName(File)}:{Line}";
}

/// <summary>
/// Maps a code address in a firmware.dll (at the base it was loaded at in the crashed Runner) to a
/// function and source line, using the in-box dbghelp.dll and the PDB next to the DLL (ADR-002).
/// </summary>
public static partial class FirmwareSymbols
{
    private const uint SymOptUndname = 0x2;
    private const uint SymOptLoadLines = 0x10;
    private const uint SymOptFailCriticalErrors = 0x200;
    private const uint SymOptNoPrompts = 0x80000;
    private const int SymbolInfoSize = 88;
    private const int MaxName = 512;

    private static readonly Lock Gate = new();
    private static int _session;

    public static unsafe SourceLocation? Resolve(string firmwarePath, ulong loadBase, ulong address)
    {
        if (!File.Exists(firmwarePath) || address < loadBase)
        {
            return null;
        }

        lock (Gate) // dbghelp is single-threaded
        {
            // dbghelp only needs a unique id here, not a real process handle, since nothing is invaded.
            nint session = 0x41520000 + Interlocked.Increment(ref _session);
            _ = SymSetOptions(SymOptUndname | SymOptLoadLines | SymOptFailCriticalErrors | SymOptNoPrompts); // returns the previous options
            if (!SymInitialize(session, Path.GetDirectoryName(firmwarePath), false))
            {
                return null;
            }

            try
            {
                if (SymLoadModuleEx(session, 0, firmwarePath, null, loadBase, 0, 0, 0) == 0)
                {
                    return null;
                }

                string? function = null;
                byte* symbol = stackalloc byte[SymbolInfoSize + (MaxName * 2)];
                new Span<byte>(symbol, SymbolInfoSize + (MaxName * 2)).Clear();
                *(uint*)symbol = SymbolInfoSize;
                *(uint*)(symbol + 80) = MaxName;
                if (SymFromAddr(session, address, out _, symbol))
                {
                    function = new string((char*)(symbol + 84), 0, (int)Math.Min(*(uint*)(symbol + 76), MaxName));
                }

                var line = new LineInfo { SizeOfStruct = (uint)sizeof(LineInfo) };
                if (SymGetLineFromAddr(session, address, out _, ref line))
                {
                    return new SourceLocation(function, Marshal.PtrToStringUni(line.FileName), (int)line.LineNumber);
                }

                return function is null ? null : new SourceLocation(function, null, 0);
            }
            finally
            {
                SymCleanup(session);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LineInfo
    {
        public uint SizeOfStruct;
        public nint Key;
        public uint LineNumber;
        public nint FileName;
        public ulong Address;
    }

    [LibraryImport("dbghelp.dll")]
    private static partial uint SymSetOptions(uint options);

    [LibraryImport("dbghelp.dll", EntryPoint = "SymInitializeW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SymInitialize(nint process, string? searchPath, [MarshalAs(UnmanagedType.Bool)] bool invadeProcess);

    [LibraryImport("dbghelp.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SymCleanup(nint process);

    [LibraryImport("dbghelp.dll", EntryPoint = "SymLoadModuleExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial ulong SymLoadModuleEx(nint process, nint file, string imageName, string? moduleName, ulong baseOfDll, uint dllSize, nint data, uint flags);

    [LibraryImport("dbghelp.dll", EntryPoint = "SymFromAddrW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SymFromAddr(nint process, ulong address, out ulong displacement, byte* symbol);

    [LibraryImport("dbghelp.dll", EntryPoint = "SymGetLineFromAddrW64")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SymGetLineFromAddr(nint process, ulong address, out uint displacement, ref LineInfo line);
}
