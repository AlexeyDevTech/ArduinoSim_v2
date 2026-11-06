using System.Net;
using System.Net.Http.Headers;

namespace ArduSim.Build.Libraries;

/// <param name="Fetched">When this copy of the catalog came from the network.</param>
/// <param name="IsStale">It could not be refreshed, and this is the saved copy.</param>
public sealed record CatalogLoad(LibraryCatalog Catalog, DateTime Fetched, bool IsStale);

/// <param name="Done">Bytes so far.</param>
/// <param name="Total">Bytes in all, when the server says.</param>
public sealed record CatalogProgress(long Done, long? Total);

/// <summary>Fetches the Arduino library index, at most once a day, and keeps a copy with its ETag (ADR-018).</summary>
public sealed class LibraryCatalogClient(HttpClient http, string cacheDirectory)
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromDays(1);

    public static Uri IndexUrl { get; } = new("https://downloads.arduino.cc/libraries/library_index.json.gz");

    private string CachePath => Path.Combine(cacheDirectory, "library_index.json.gz");

    private string TagPath => CachePath + ".etag";

    /// <param name="refresh">Ask the network even if the copy is recent.</param>
    public async Task<CatalogLoad> LoadAsync(bool refresh, IProgress<CatalogProgress>? progress, CancellationToken cancellationToken)
    {
        bool saved = File.Exists(CachePath);
        if (saved && !refresh && DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath) < FreshFor && TryParse(out LibraryCatalog? recent))
        {
            return new CatalogLoad(recent, File.GetLastWriteTimeUtc(CachePath), IsStale: false);
        }

        try
        {
            await FetchAsync(saved, progress, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return saved && TryParse(out LibraryCatalog? old)
                ? new CatalogLoad(old, File.GetLastWriteTimeUtc(CachePath), IsStale: true)
                : throw new LibraryException(LibraryFailure.CatalogUnavailable, $"{IndexUrl}: {e.Message}", e);
        }

        return TryParse(out LibraryCatalog? fetched)
            ? new CatalogLoad(fetched, File.GetLastWriteTimeUtc(CachePath), IsStale: false)
            : throw new LibraryException(LibraryFailure.CatalogUnavailable, $"{IndexUrl}: what came back is not the catalog.");
    }

    private async Task FetchAsync(bool saved, IProgress<CatalogProgress>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IndexUrl);
        if (saved && File.Exists(TagPath))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", await File.ReadAllTextAsync(TagPath, cancellationToken));
        }

        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            File.SetLastWriteTimeUtc(CachePath, DateTime.UtcNow);
            return;
        }

        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(cacheDirectory);
        string partial = CachePath + ".partial";
        await using (Stream body = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            long? total = response.Content.Headers.ContentLength;
            byte[] buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                progress?.Report(new CatalogProgress(done, total));
            }
        }

        File.Move(partial, CachePath, overwrite: true);
        EntityTagHeaderValue? tag = response.Headers.ETag;
        if (tag is null)
        {
            File.Delete(TagPath);
        }
        else
        {
            await File.WriteAllTextAsync(TagPath, tag.ToString(), cancellationToken);
        }
    }

    private bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LibraryCatalog? catalog)
    {
        try
        {
            catalog = LibraryCatalog.ParseGzip(CachePath);
            return catalog.Libraries.Count > 0;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            catalog = null;
            return false;
        }
    }
}
