using System.IO.Compression;
using System.Security.Cryptography;

namespace ArduSim.Build.Libraries;

public enum LibraryFailure
{
    /// <summary>The catalog could not be fetched and no copy is saved.</summary>
    CatalogUnavailable,

    /// <summary>The download failed.</summary>
    Download,

    /// <summary>The download is not what the catalog says.</summary>
    Checksum,

    /// <summary>The archive is damaged, unsafe or too large.</summary>
    BadArchive,

    /// <summary>There is no library in this folder or archive.</summary>
    NotALibrary,

    /// <summary>The folder chosen already is in the project.</summary>
    AlreadyInProject,

    /// <summary>The disk refused.</summary>
    Disk,
}

public sealed class LibraryException : Exception
{
    public LibraryException()
    {
    }

    public LibraryException(string message)
        : base(message)
    {
    }

    public LibraryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LibraryException(LibraryFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public LibraryFailure Failure { get; }
}

/// <param name="Done">Bytes so far.</param>
/// <param name="Total">Bytes the catalog says it has.</param>
public sealed record LibraryDownloadProgress(string Name, long Done, long Total);

/// <summary>
/// Puts libraries into a project's <c>libs</c> folder (ADR-018). Everything happens in a staging folder
/// beside it first: only a complete, verified library is moved into place, so a failure at any stage
/// leaves the project as it was.
/// </summary>
/// <param name="recycle">What happens to the copy a new one replaces (the App: the Recycle Bin).</param>
public sealed class LibraryInstaller(HttpClient http, string libsDirectory, Action<string>? recycle = null)
{
    private const long MaxUnpackedBytes = 1L << 30;

    private readonly Action<string> _recycle = recycle ?? (path => Directory.Delete(path, recursive: true));

    /// <summary>Downloads a catalog release, checks it and installs it.</summary>
    public async Task<LibraryInfo> InstallAsync(CatalogLibrary library, CatalogRelease release, IProgress<LibraryDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(release);
        string staging = NewStaging();
        try
        {
            string archive = Path.Combine(staging, "download.zip");
            await DownloadAsync(library.Name, release, archive, progress, cancellationToken);
            string unpacked = Path.Combine(staging, "unpacked");
            Unpack(archive, unpacked, cancellationToken);
            IReadOnlyList<string> found = LibraryFolders.Find(unpacked);
            if (found.Count == 0)
            {
                throw new LibraryException(LibraryFailure.NotALibrary, $"{release.FileName} has no library in it.");
            }

            string normal = LibraryFolders.Normalize(library.Name);
            string root = found.FirstOrDefault(f => LibraryFolders.Normalize(LibraryFolders.Read(f).Name) == normal) ?? found[0];
            return MoveIntoPlace(root, library.Name);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>Installs what a ZIP holds: one library, usually in a wrapper folder such as <c>Foo-master</c>.</summary>
    public Task<IReadOnlyList<LibraryInfo>> AddZipAsync(string zip, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<LibraryInfo>>(
        () =>
        {
            string staging = NewStaging();
            try
            {
                string unpacked = Path.Combine(staging, "unpacked");
                Unpack(zip, unpacked, cancellationToken);
                return MoveFound(unpacked, Path.GetFileName(zip));
            }
            finally
            {
                TryDelete(staging);
            }
        },
        cancellationToken);

    /// <summary>Copies what a folder holds: the library it is, or the libraries in it.</summary>
    public Task<IReadOnlyList<LibraryInfo>> AddFolderAsync(string folder, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<LibraryInfo>>(
        () =>
        {
            ArgumentNullException.ThrowIfNull(folder);
            string own = Path.GetFullPath(libsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string[] found;
            try
            {
                found = [.. LibraryFolders.Find(folder).Where(f => !(Path.GetFullPath(f) + Path.DirectorySeparatorChar).StartsWith(own, StringComparison.OrdinalIgnoreCase))];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new LibraryException(LibraryFailure.Disk, $"{folder}: {e.Message}", e);
            }

            if (found.Length == 0)
            {
                bool inside = (Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(own, StringComparison.OrdinalIgnoreCase);
                throw inside
                    ? new LibraryException(LibraryFailure.AlreadyInProject, $"{folder} already is in the project.")
                    : new LibraryException(LibraryFailure.NotALibrary, $"{folder} has no library in it.");
            }

            var added = new List<LibraryInfo>();
            foreach (string library in found)
            {
                string staging = NewStaging();
                try
                {
                    string copy = Path.Combine(staging, "copy");
                    Copy(library, copy, cancellationToken);
                    added.Add(MoveIntoPlace(copy, LibraryFolders.Read(library).Name));
                }
                finally
                {
                    TryDelete(staging);
                }
            }

            return added;
        },
        cancellationToken);

    private List<LibraryInfo> MoveFound(string unpacked, string source)
    {
        IReadOnlyList<string> found = LibraryFolders.Find(unpacked);
        if (found.Count == 0)
        {
            throw new LibraryException(LibraryFailure.NotALibrary, $"{source} has no library in it.");
        }

        return [.. found.Select(f => MoveIntoPlace(f, LibraryFolders.Read(f).Name))];
    }

    private LibraryInfo MoveIntoPlace(string prepared, string name)
    {
        string target = Path.Combine(libsDirectory, LibraryFolders.FolderNameFor(name));
        try
        {
            if (Directory.Exists(target))
            {
                _recycle(target);
            }

            Directory.Move(prepared, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException(LibraryFailure.Disk, $"{target}: {e.Message}", e);
        }

        return LibraryFolders.Read(target);
    }

    private string NewStaging()
    {
        string staging = Path.Combine(libsDirectory, ".staging-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(staging);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException(LibraryFailure.Disk, $"{libsDirectory}: {e.Message}", e);
        }

        return staging;
    }

    private async Task DownloadAsync(string name, CatalogRelease release, string path, IProgress<LibraryDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                progress?.Report(new LibraryDownloadProgress(name, done, release.Size));
            }

            string actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LibraryException(LibraryFailure.Checksum, $"{release.FileName}: SHA-256 is {actual}, the catalog says {release.Sha256}.");
            }
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new LibraryException(LibraryFailure.Download, $"{release.Url}: {e.Message}", e);
        }
    }

    private static void Unpack(string archive, string destination, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(archive);
            long total = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new LibraryException(LibraryFailure.BadArchive, $"{archive}: the entry {entry.FullName} would land outside its folder.");
                }

                total += entry.Length;
                if (total > MaxUnpackedBytes)
                {
                    throw new LibraryException(LibraryFailure.BadArchive, $"{archive} unpacks to more than 1 GiB.");
                }

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
        }
        catch (InvalidDataException e)
        {
            throw new LibraryException(LibraryFailure.BadArchive, $"{archive}: {e.Message}", e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException(LibraryFailure.Disk, $"{archive}: {e.Message}", e);
        }
    }

    private static void Copy(string source, string destination, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (string folder in Directory.EnumerateDirectories(source))
            {
                if (!Path.GetFileName(folder).Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    Copy(folder, Path.Combine(destination, Path.GetFileName(folder)), cancellationToken);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException(LibraryFailure.Disk, $"{source}: {e.Message}", e);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Could not remove {0}: {1}", directory, e.Message);
        }
    }
}
