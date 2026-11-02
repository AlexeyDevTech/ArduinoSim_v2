using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using ArduSim.Build.Processes;

namespace ArduSim.Build.Toolchains;

/// <summary>
/// Finds or installs the pinned toolchain under <c>%LOCALAPPDATA%\ArduSim\toolchain\&lt;id&gt;</c>:
/// resumable download, SHA-256 check, filtered extraction, smoke compile, atomic rename.
/// Safe to call from several processes at once (a lock file serializes installs).
/// </summary>
public sealed class ToolchainManager(string toolchainsDirectory, HttpClient http, ToolchainManifest? manifest = null)
{
    private const string MarkerFile = "ardusim-toolchain.ok";

    public ToolchainManifest Manifest { get; } = manifest ?? ToolchainManifest.LlvmMingw;

    private string InstallDirectory => Path.Combine(toolchainsDirectory, Manifest.Id);

    public Toolchain? FindInstalled()
    {
        var toolchain = new Toolchain(Manifest.Id, InstallDirectory);
        return File.Exists(Path.Combine(InstallDirectory, MarkerFile)) && toolchain.IsComplete ? toolchain : null;
    }

    public async Task<Toolchain> EnsureInstalledAsync(IProgress<ToolchainProgress>? progress, CancellationToken cancellationToken)
    {
        if (FindInstalled() is { } installed)
        {
            return installed;
        }

        Directory.CreateDirectory(toolchainsDirectory);
        using FileStream installLock = await AcquireLockAsync(cancellationToken);
        if (FindInstalled() is { } installedMeanwhile)
        {
            return installedMeanwhile;
        }

        string archive = await DownloadAsync(progress, cancellationToken);
        await VerifyAsync(archive, progress, cancellationToken);
        string staging = Extract(archive, progress, cancellationToken);
        await SmokeTestAsync(new Toolchain(Manifest.Id, staging), progress, cancellationToken);

        if (Directory.Exists(InstallDirectory))
        {
            Directory.Delete(InstallDirectory, recursive: true);
        }

        Directory.Move(staging, InstallDirectory);
        await File.WriteAllTextAsync(Path.Combine(InstallDirectory, MarkerFile), Manifest.Sha256, cancellationToken);
        File.Delete(archive);
        return FindInstalled() ?? throw new ToolchainException(ToolchainFailure.ExtractFailed, $"Toolchain {Manifest.Id} is incomplete after installation.");
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(toolchainsDirectory, "install.lock");
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
    }

    private async Task<string> DownloadAsync(IProgress<ToolchainProgress>? progress, CancellationToken cancellationToken)
    {
        string archive = Path.Combine(toolchainsDirectory, Manifest.FileName);
        long existing = File.Exists(archive) ? new FileInfo(archive).Length : 0;
        if (existing == Manifest.SizeBytes)
        {
            return archive;
        }

        if (existing > Manifest.SizeBytes)
        {
            File.Delete(archive);
            existing = 0;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Manifest.Url);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            bool resumed = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!resumed)
            {
                existing = 0;
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = new FileStream(archive, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            byte[] buffer = new byte[1 << 16];
            long done = existing;
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                progress?.Report(new ToolchainProgress(ToolchainStage.Downloading, done, Manifest.SizeBytes));
            }
        }
        catch (HttpRequestException e)
        {
            throw new ToolchainException(ToolchainFailure.DownloadFailed, $"Downloading {Manifest.Url} failed: {e.Message}", e);
        }
        catch (IOException e)
        {
            throw new ToolchainException(ToolchainFailure.DownloadFailed, $"Downloading {Manifest.Url} failed: {e.Message}", e);
        }

        return archive;
    }

    private async Task VerifyAsync(string archive, IProgress<ToolchainProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ToolchainProgress(ToolchainStage.Verifying, 0, Manifest.SizeBytes));
        string actual;
        await using (FileStream stream = File.OpenRead(archive))
        {
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        if (!string.Equals(actual, Manifest.Sha256, StringComparison.Ordinal))
        {
            File.Delete(archive);
            throw new ToolchainException(ToolchainFailure.ChecksumMismatch, $"{Manifest.FileName}: SHA-256 is {actual}, expected {Manifest.Sha256}. The download was deleted.");
        }

        progress?.Report(new ToolchainProgress(ToolchainStage.Verifying, Manifest.SizeBytes, Manifest.SizeBytes));
    }

    private string Extract(string archive, IProgress<ToolchainProgress>? progress, CancellationToken cancellationToken)
    {
        string staging = InstallDirectory + ".partial";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        string stagingFull = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(archive);
            int done = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                done++;
                if (!entry.FullName.StartsWith(Manifest.ArchiveRoot, StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = entry.FullName[Manifest.ArchiveRoot.Length..];
                if (relative.Length == 0 || Manifest.IsExcluded(relative))
                {
                    continue;
                }

                string target = Path.GetFullPath(Path.Combine(staging, relative));
                if (!target.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ToolchainException(ToolchainFailure.ExtractFailed, $"Archive entry escapes the install folder: {entry.FullName}");
                }

                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                if (done % 200 == 0)
                {
                    progress?.Report(new ToolchainProgress(ToolchainStage.Extracting, done, zip.Entries.Count));
                }
            }

            progress?.Report(new ToolchainProgress(ToolchainStage.Extracting, zip.Entries.Count, zip.Entries.Count));
        }
        catch (InvalidDataException e)
        {
            throw new ToolchainException(ToolchainFailure.ExtractFailed, $"{Manifest.FileName} is not a valid zip: {e.Message}", e);
        }
        catch (IOException e)
        {
            throw new ToolchainException(ToolchainFailure.ExtractFailed, $"Extracting {Manifest.FileName} failed: {e.Message}", e);
        }

        return staging;
    }

    private static async Task SmokeTestAsync(Toolchain toolchain, IProgress<ToolchainProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ToolchainProgress(ToolchainStage.Testing, 0, 1));
        string dir = Path.Combine(Path.GetTempPath(), "ardusim-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string source = Path.Combine(dir, "smoke.cpp");
            await File.WriteAllTextAsync(source, "extern \"C\" __declspec(dllexport) int ardusim_smoke(int v) { return v + 1; }\n", cancellationToken);
            string dll = Path.Combine(dir, "smoke.dll");
            ProcessResult result = await ProcessRunner.RunAsync(
                toolchain.CxxCompiler,
                ["-std=gnu++17", "-fno-exceptions", "-fno-rtti", "-O1", "-shared", "-static", source, "-o", dll],
                dir,
                cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(dll))
            {
                throw new ToolchainException(ToolchainFailure.SmokeTestFailed, $"The toolchain could not build a test DLL (exit code {result.ExitCode}):\n{result.Output}");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        progress?.Report(new ToolchainProgress(ToolchainStage.Testing, 1, 1));
    }
}
