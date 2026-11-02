namespace ArduSim.Build.Toolchains;

/// <summary>An installed clang toolchain targeting x86_64-w64-mingw32 (ADR-002).</summary>
public sealed record Toolchain(string Id, string Root)
{
    public string CxxCompiler => Path.Combine(Root, "bin", "x86_64-w64-mingw32-clang++.exe");

    public string CCompiler => Path.Combine(Root, "bin", "x86_64-w64-mingw32-clang.exe");

    public bool IsComplete => File.Exists(CxxCompiler) && File.Exists(CCompiler);
}
