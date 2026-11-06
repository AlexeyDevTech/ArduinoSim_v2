using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ArduSim.Build.Libraries;

namespace ArduSim.Build.Tests;

/// <summary>The catalog and the installer (ADR-018), with a fake network: nothing here touches the real one.</summary>
public sealed class LibraryManagementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ardusim-libs-" + Guid.NewGuid().ToString("N")[..8]);

    public LibraryManagementTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Libs => Path.Combine(_root, "project", "libs");

    private const string Index = """
        { "libraries": [
          { "name": "Foo Bar", "version": "1.9.0", "author": "Jane <jane@example.com>, John", "sentence": "Drives a foo.", "website": "https://example.com", "architectures": ["*"],
            "providesIncludes": ["FooBar.h", "FooExtra.h"], "dependencies": [{ "name": "Baz" }], "url": "https://downloads.example/foo-1.9.0.zip", "archiveFileName": "foo-1.9.0.zip", "size": 10, "checksum": "SHA-256:AB" },
          { "name": "Foo Bar", "version": "1.10.0", "author": "Jane <jane@example.com>, John", "sentence": "Drives a foo better.", "website": "", "architectures": ["*"],
            "providesIncludes": ["FooBar.h"], "dependencies": [{ "name": "Baz" }, { "name": "Nowhere" }], "url": "https://downloads.example/foo-1.10.0.zip", "archiveFileName": "foo-1.10.0.zip", "size": 10, "checksum": "SHA-256:CD" },
          { "name": "Foo Bar", "version": "1.10.0-rc1", "author": "", "sentence": "", "website": "", "architectures": ["*"], "url": "https://downloads.example/foo-rc.zip", "archiveFileName": "foo-rc.zip", "size": 1, "checksum": "SHA-256:EF" },
          { "name": "Baz", "version": "2.0.0", "author": "Bo", "sentence": "Needs qux, and foo again.", "website": "", "architectures": ["avr"], "dependencies": [{ "name": "Qux" }, { "name": "Foo Bar" }], "url": "https://downloads.example/baz.zip", "archiveFileName": "baz.zip", "size": 1, "checksum": "SHA-256:01" },
          { "name": "Qux", "version": "0.1", "author": "Bo", "sentence": "A sensor of foo.", "website": "", "architectures": ["*"], "url": "https://downloads.example/qux.zip", "archiveFileName": "qux.zip", "size": 1, "checksum": "SHA-256:02" },
          { "name": "FooBar Tools", "version": "1", "author": "Bo", "sentence": "Tools.", "website": "", "architectures": ["*"], "url": "https://downloads.example/t.zip", "archiveFileName": "t.zip", "size": 1, "checksum": "SHA-256:03" },
          { "name": "Insecure", "version": "1", "author": "", "sentence": "", "website": "", "architectures": ["*"], "url": "http://downloads.example/i.zip", "archiveFileName": "i.zip", "size": 1, "checksum": "SHA-256:04" }
        ] }
        """;

    private static LibraryCatalog Catalog() => LibraryCatalog.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Index)));

    [Fact]
    public void Releases_group_by_library_newest_first_and_a_prerelease_comes_before_its_release()
    {
        CatalogLibrary foo = Catalog().Find("Foo Bar")!;

        Assert.Equal(["1.10.0", "1.10.0-rc1", "1.9.0"], foo.Releases.Select(r => r.Version));
        Assert.Equal("Drives a foo better.", foo.Sentence);
        Assert.Equal("Jane, John", foo.Author);
        Assert.Equal("CD", foo.Latest.Sha256);
        Assert.Equal(["Baz", "Nowhere"], foo.Latest.Dependencies);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]
    [InlineData("1.0.0-beta", "1.0.0-rc", -1)]
    [InlineData("v2", "v10", 1)]
    public void Versions_compare_by_number(string a, string b, int order) =>
        Assert.Equal(order, Math.Sign(LibraryCatalog.CompareVersions(a, b)));

    [Fact]
    public void An_exact_name_wins_over_one_that_differs_only_in_case()
    {
        LibraryCatalog catalog = LibraryCatalog.Parse(new MemoryStream(Encoding.UTF8.GetBytes("""
            { "libraries": [
              { "name": "SSD1306", "version": "1", "url": "https://x.example/a.zip", "architectures": ["*"] },
              { "name": "ssd1306", "version": "2", "url": "https://x.example/b.zip", "architectures": ["*"] }
            ] }
            """)));

        Assert.Equal("1", catalog.Find("SSD1306")!.Latest.Version);
        Assert.Equal("2", catalog.Find("ssd1306")!.Latest.Version);
        Assert.NotNull(catalog.Find("Ssd1306"));
    }

    [Fact]
    public async Task A_folder_that_is_not_there_is_a_failure_with_a_reason()
    {
        var failure = await Assert.ThrowsAsync<LibraryException>(() => Installer([]).AddFolderAsync(Path.Combine(_root, "gone"), TestContext.Current.CancellationToken));

        Assert.Equal(LibraryFailure.Disk, failure.Failure);
    }

    [Fact]
    public void A_library_without_https_cannot_be_installed_so_it_is_not_listed() =>
        Assert.Null(Catalog().Find("Insecure"));

    [Fact]
    public void Search_ranks_the_name_before_the_description()
    {
        LibraryCatalog catalog = Catalog();

        Assert.Equal(["Foo Bar", "FooBar Tools", "Baz", "Qux"], catalog.Search("foo").Select(l => l.Name));
        Assert.Equal("Foo Bar", catalog.Search("foo_bar")[0].Name);
        Assert.Equal(["Foo Bar"], catalog.Search("foo better").Select(l => l.Name));
        Assert.Empty(catalog.Search("nothing like this"));
        Assert.Equal(catalog.Libraries.Count, catalog.Search("  ").Count);
    }

    [Fact]
    public void A_header_finds_what_provides_it_or_a_library_named_after_it()
    {
        LibraryCatalog catalog = Catalog();

        Assert.Equal(["Foo Bar"], catalog.Search("FooBar.h").Select(l => l.Name));
        Assert.Equal(["Qux"], catalog.Search("Qux.h").Select(l => l.Name));
        Assert.Empty(catalog.Search("Missing.h"));
    }

    [Fact]
    public void A_plan_brings_what_is_missing_once_and_says_what_the_catalog_does_not_have()
    {
        LibraryCatalog catalog = Catalog();
        CatalogLibrary foo = catalog.Find("Foo Bar")!;

        InstallPlan all = catalog.Plan(foo, foo.Latest, _ => false);
        Assert.Equal(["Foo Bar", "Baz", "Qux"], all.Items.Select(i => i.Library.Name));
        Assert.Equal(["Nowhere"], all.Unknown);

        InstallPlan some = catalog.Plan(foo, foo.Latest, name => name == "Baz");
        Assert.Equal(["Foo Bar"], some.Items.Select(i => i.Library.Name));

        CatalogRelease older = foo.Releases[^1];
        Assert.Empty(catalog.Plan(foo, older, _ => false).Unknown);
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static CatalogRelease Release(byte[] content, string? sha = null) => new(
        "1.0.0",
        new Uri("https://downloads.example/lib.zip"),
        "lib.zip",
        content.Length,
        sha ?? Convert.ToHexString(SHA256.HashData(content)),
        [],
        [],
        ["*"]);

    private static CatalogLibrary Library(string name, CatalogRelease release) => new(name, "", "", "", [release]);

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private LibraryInstaller Installer(byte[] served, Action<string>? recycle = null)
    {
        Directory.CreateDirectory(Libs);
#pragma warning disable CA2000 // the handler lives as long as the test
        return new LibraryInstaller(new HttpClient(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(served) })), Libs, recycle);
#pragma warning restore CA2000
    }

    private static readonly (string, string)[] FooFiles =
    [
        ("Foo_Bar-1.0.0/library.properties", "name=Foo Bar\nversion=1.0.0\n"),
        ("Foo_Bar-1.0.0/src/FooBar.h", "// foo\n"),
        ("Foo_Bar-1.0.0/src/FooBar.cpp", "// foo\n"),
    ];

    [Fact]
    public async Task A_release_is_checked_unpacked_and_named_as_the_arduino_ide_names_it()
    {
        byte[] zip = Zip(FooFiles);
        var progress = new List<LibraryDownloadProgress>();

        LibraryInfo installed = await Installer(zip).InstallAsync(Library("Foo Bar", Release(zip)), Release(zip), new SyncProgress<LibraryDownloadProgress>(progress.Add), TestContext.Current.CancellationToken);

        Assert.Equal("Foo Bar", installed.Name);
        Assert.Equal("1.0.0", installed.Version);
        Assert.Equal(Path.Combine(Libs, "Foo_Bar"), installed.Directory);
        Assert.True(File.Exists(Path.Combine(Libs, "Foo_Bar", "src", "FooBar.h")));
        Assert.Equal(zip.Length, progress[^1].Done);
        Assert.Equal(["Foo_Bar"], Directory.EnumerateDirectories(Libs).Select(Path.GetFileName));
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [Fact]
    public async Task A_wrong_checksum_keeps_nothing_and_leaves_the_old_copy_in_place()
    {
        byte[] zip = Zip(FooFiles);
        string old = Path.Combine(Libs, "Foo_Bar");
        Directory.CreateDirectory(old);
        await File.WriteAllTextAsync(Path.Combine(old, "Old.h"), "", TestContext.Current.CancellationToken);
        CatalogRelease release = Release(zip, sha: "00");

        var failure = await Assert.ThrowsAsync<LibraryException>(() => Installer(zip).InstallAsync(Library("Foo Bar", release), release, null, TestContext.Current.CancellationToken));

        Assert.Equal(LibraryFailure.Checksum, failure.Failure);
        Assert.True(File.Exists(Path.Combine(old, "Old.h")));
        Assert.Equal(["Foo_Bar"], Directory.EnumerateDirectories(Libs).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_new_copy_replaces_the_old_only_when_it_is_ready_and_the_old_goes_where_it_is_told()
    {
        byte[] zip = Zip(FooFiles);
        string old = Path.Combine(Libs, "Foo_Bar");
        Directory.CreateDirectory(old);
        await File.WriteAllTextAsync(Path.Combine(old, "Old.h"), "", TestContext.Current.CancellationToken);
        var recycled = new List<string>();

        await Installer(zip, path => { recycled.Add(path); Directory.Delete(path, recursive: true); })
            .InstallAsync(Library("Foo Bar", Release(zip)), Release(zip), null, TestContext.Current.CancellationToken);

        Assert.Equal([old], recycled);
        Assert.False(File.Exists(Path.Combine(old, "Old.h")));
        Assert.True(File.Exists(Path.Combine(old, "src", "FooBar.h")));
    }

    [Fact]
    public async Task An_entry_that_would_land_outside_is_refused_and_nothing_is_written()
    {
        string zipPath = Path.Combine(_root, "evil.zip");
        await File.WriteAllBytesAsync(zipPath, Zip(("../../evil.h", "x"), ("Foo/library.properties", "name=Foo\n")), TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<LibraryException>(() => Installer([]).AddZipAsync(zipPath, TestContext.Current.CancellationToken));

        Assert.Equal(LibraryFailure.BadArchive, failure.Failure);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Libs));
        Assert.False(File.Exists(Path.Combine(_root, "project", "evil.h")));
    }

    [Fact]
    public async Task A_damaged_zip_says_so()
    {
        string zipPath = Path.Combine(_root, "bad.zip");
        await File.WriteAllTextAsync(zipPath, "not a zip", TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<LibraryException>(() => Installer([]).AddZipAsync(zipPath, TestContext.Current.CancellationToken));

        Assert.Equal(LibraryFailure.BadArchive, failure.Failure);
    }

    [Fact]
    public async Task A_github_zip_without_properties_loses_its_branch_suffix()
    {
        string zipPath = Path.Combine(_root, "Thing-master.zip");
        await File.WriteAllBytesAsync(zipPath, Zip(("Thing-master/Thing.h", "// thing\n"), ("Thing-master/README.md", "")), TestContext.Current.CancellationToken);

        LibraryInfo added = Assert.Single(await Installer([]).AddZipAsync(zipPath, TestContext.Current.CancellationToken));

        Assert.Equal("Thing", added.Name);
        Assert.True(File.Exists(Path.Combine(Libs, "Thing", "Thing.h")));
    }

    private string MakeFolder(string name, params string[] files)
    {
        string folder = Path.Combine(_root, name);
        foreach (string file in files)
        {
            string path = Path.Combine(folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.EndsWith("properties", StringComparison.Ordinal) ? "name=From Folder\nversion=3\n" : "");
        }

        return folder;
    }

    [Fact]
    public async Task A_folder_is_copied_without_its_git_history()
    {
        string folder = MakeFolder("mine", "library.properties", "src/Mine.h", ".git/config");

        LibraryInfo added = Assert.Single(await Installer([]).AddFolderAsync(folder, TestContext.Current.CancellationToken));

        Assert.Equal("From Folder", added.Name);
        Assert.Equal("3", added.Version);
        Assert.True(File.Exists(Path.Combine(Libs, "From_Folder", "src", "Mine.h")));
        Assert.False(Directory.Exists(Path.Combine(Libs, "From_Folder", ".git")));
        Assert.True(File.Exists(Path.Combine(folder, "src", "Mine.h")));
    }

    [Fact]
    public async Task A_folder_of_libraries_adds_each_of_them()
    {
        string folder = MakeFolder("many", "One/One.h", "Two/src/Two.h", "NotOne/readme.txt");

        IReadOnlyList<LibraryInfo> added = await Installer([]).AddFolderAsync(folder, TestContext.Current.CancellationToken);

        Assert.Equal(["One", "Two"], added.Select(l => l.Name));
    }

    [Fact]
    public async Task A_sketch_is_not_a_library_and_neither_is_an_empty_folder()
    {
        string sketch = MakeFolder("sketch", "sketch.ino", "config.h");

        var failure = await Assert.ThrowsAsync<LibraryException>(() => Installer([]).AddFolderAsync(sketch, TestContext.Current.CancellationToken));

        Assert.Equal(LibraryFailure.NotALibrary, failure.Failure);
    }

    [Fact]
    public async Task The_projects_own_library_folder_cannot_be_added_to_itself()
    {
        Directory.CreateDirectory(Path.Combine(Libs, "Mine"));
        await File.WriteAllTextAsync(Path.Combine(Libs, "Mine", "Mine.h"), "", TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<LibraryException>(() => Installer([]).AddFolderAsync(Path.Combine(Libs, "Mine"), TestContext.Current.CancellationToken));

        Assert.Equal(LibraryFailure.AlreadyInProject, failure.Failure);
    }

    [Theory]
    [InlineData("Adafruit NeoPixel", "Adafruit_NeoPixel")]
    [InlineData("a/b:c", "a_b_c")]
    [InlineData("Trailing. ", "Trailing")]
    public void Folder_names_are_safe(string name, string folder) => Assert.Equal(folder, LibraryFolders.FolderNameFor(name));

    private static byte[] Gzip(string text)
    {
        using var stream = new MemoryStream();
        using (var zip = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true))
        {
            zip.Write(Encoding.UTF8.GetBytes(text));
        }

        return stream.ToArray();
    }

    private static HttpResponseMessage Tagged(byte[] body, string tag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(tag);
        return response;
    }

    [Fact]
    public async Task The_catalog_is_fetched_once_a_day_and_kept_with_its_tag()
    {
        string? askedWith = null;
        var http = new FakeHttp(request =>
        {
            askedWith = request.Headers.TryGetValues("If-None-Match", out IEnumerable<string>? tags) ? tags.Single() : null;
            return askedWith is null ? Tagged(Gzip(Index), "\"v1\"") : new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        using var client = new HttpClient(http);
        var catalog = new LibraryCatalogClient(client, Path.Combine(_root, "index"));

        CatalogLoad first = await catalog.LoadAsync(refresh: false, null, TestContext.Current.CancellationToken);
        Assert.NotNull(first.Catalog.Find("Baz"));
        Assert.False(first.IsStale);
        Assert.Equal(1, http.Calls);

        await catalog.LoadAsync(refresh: false, null, TestContext.Current.CancellationToken);
        Assert.Equal(1, http.Calls);

        CatalogLoad asked = await catalog.LoadAsync(refresh: true, null, TestContext.Current.CancellationToken);
        Assert.Equal(2, http.Calls);
        Assert.Equal("\"v1\"", askedWith);
        Assert.NotNull(asked.Catalog.Find("Baz"));
    }

    [Fact]
    public async Task Without_a_network_the_saved_catalog_is_used_and_says_so_and_without_one_it_fails()
    {
        bool online = true;
        var http = new FakeHttp(_ => online ? Tagged(Gzip(Index), "\"v1\"") : throw new HttpRequestException("no network"));
        using var client = new HttpClient(http);
        var catalog = new LibraryCatalogClient(client, Path.Combine(_root, "index"));
        var nothing = new LibraryCatalogClient(client, Path.Combine(_root, "other"));

        await catalog.LoadAsync(refresh: false, null, TestContext.Current.CancellationToken);
        online = false;

        CatalogLoad stale = await catalog.LoadAsync(refresh: true, null, TestContext.Current.CancellationToken);
        Assert.True(stale.IsStale);
        Assert.NotNull(stale.Catalog.Find("Baz"));

        var failure = await Assert.ThrowsAsync<LibraryException>(() => nothing.LoadAsync(refresh: false, null, TestContext.Current.CancellationToken));
        Assert.Equal(LibraryFailure.CatalogUnavailable, failure.Failure);
    }

    [Fact]
    public async Task A_damaged_saved_catalog_is_fetched_again()
    {
        string index = Path.Combine(_root, "index");
        Directory.CreateDirectory(index);
        await File.WriteAllTextAsync(Path.Combine(index, "library_index.json.gz"), "garbage", TestContext.Current.CancellationToken);
        var http = new FakeHttp(_ => Tagged(Gzip(Index), "\"v1\""));
        using var client = new HttpClient(http);

        CatalogLoad load = await new LibraryCatalogClient(client, index).LoadAsync(refresh: false, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, http.Calls);
        Assert.NotNull(load.Catalog.Find("Baz"));
    }
}
