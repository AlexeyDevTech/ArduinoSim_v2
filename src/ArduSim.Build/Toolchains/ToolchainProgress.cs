namespace ArduSim.Build.Toolchains;

public enum ToolchainStage
{
    Downloading,
    Verifying,
    Extracting,
    Testing,
}

/// <summary>Progress of a toolchain install; <see cref="Total"/> is 0 when unknown.</summary>
public sealed record ToolchainProgress(ToolchainStage Stage, long Completed, long Total);

public enum ToolchainFailure
{
    DownloadFailed,
    ChecksumMismatch,
    ExtractFailed,
    SmokeTestFailed,
}

/// <summary>Installing the toolchain failed; the UI's next action is "re-download".</summary>
public sealed class ToolchainException : Exception
{
    public ToolchainException()
    {
    }

    public ToolchainException(string message)
        : base(message)
    {
    }

    public ToolchainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ToolchainException(ToolchainFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public ToolchainFailure Failure { get; }
}
