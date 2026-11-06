using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.Boards;
using ArduSim.Build.Compilation;
using ArduSim.Core.Projects;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArduSim.App.Tests;

/// <summary>The library dialog and the Библиотеки group (ADR-018), with a fake network.</summary>
[Collection("Main view model")]
public sealed class LibraryDialogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ardusim-libview-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Dictionary<string, byte[]> _served = new(StringComparer.Ordinal);

    public LibraryDialogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A language server may still hold a file; the temp folder is not worth failing a test.
        }
    }

    private sealed class FakeHttp(Dictionary<string, byte[]> served) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(served.TryGetValue(request.RequestUri!.AbsolutePath, out byte[]? body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Gzip(string text)
    {
        using var stream = new MemoryStream();
        using (var zip = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true))
        {
            zip.Write(Encoding.UTF8.GetBytes(text));
        }

        return stream.ToArray();
    }

    private static string Entry(string name, string version, byte[] zip, string architectures = "\"*\"", string dependencies = "") =>
        $$"""
        { "name": "{{name}}", "version": "{{version}}", "author": "Bo", "sentence": "About {{name}}.", "website": "", "architectures": [{{architectures}}],
          "dependencies": [{{dependencies}}], "providesIncludes": ["{{name.Replace(" ", "", StringComparison.Ordinal)}}.h"], "url": "https://x.example/{{name.Replace(" ", "", StringComparison.Ordinal)}}-{{version}}.zip", "archiveFileName": "a.zip", "size": {{zip.Length}},
          "checksum": "SHA-256:{{Convert.ToHexString(SHA256.HashData(zip))}}" }
        """;

    private static byte[] LibraryZip(string name, string version) => Zip(
        (name + "-" + version + "/library.properties", $"name={name}\nversion={version}\n"),
        (name + "-" + version + "/src/" + name.Replace(" ", "", StringComparison.Ordinal) + ".h", "// header\n"));

    private (LibraryService Service, string Project) Fixture()
    {
        string project = Path.Combine(_root, "proj");
        Directory.CreateDirectory(project);
        ProjectTemplate.Create(project, Nano.Id);
        byte[] gadgetOld = LibraryZip("Gadget", "1.0.0");
        byte[] gadgetNew = LibraryZip("Gadget", "2.0.0");
        byte[] helper = LibraryZip("Helper", "1.0.0");
        byte[] samd = LibraryZip("SamdOnly", "1.0.0");
        _served["/Gadget-1.0.0.zip"] = gadgetOld;
        _served["/Gadget-2.0.0.zip"] = gadgetNew;
        _served["/Helper-1.0.0.zip"] = helper;
        _served["/SamdOnly-1.0.0.zip"] = samd;
        string index = $$"""
            { "libraries": [
              {{Entry("Gadget", "1.0.0", gadgetOld, dependencies: "{\"name\":\"Helper\"},{\"name\":\"LiquidCrystal\"}")}},
              {{Entry("Gadget", "2.0.0", gadgetNew, dependencies: "{\"name\":\"Helper\"},{\"name\":\"LiquidCrystal\"}")}},
              {{Entry("Helper", "1.0.0", helper)}},
              {{Entry("SamdOnly", "1.0.0", samd, "\"samd\"")}},
              {{Entry("LiquidCrystal", "9.9.9", gadgetNew)}}
            ] }
            """;
        _served["/libraries/library_index.json.gz"] = Gzip(index);
#pragma warning disable CA2000 // lives as long as the test
        var http = new HttpClient(new FakeHttp(_served));
#pragma warning restore CA2000
        var service = new LibraryService(http, Path.Combine(_root, "index"), project, ProjectFile.Load(project), Nano.Profile, path => Directory.Delete(path, recursive: true));
        return (service, project);
    }

    private static LibraryRow Row(LibraryDialogViewModel dialog, string name) => dialog.Rows.Single(r => r.Name == name);

    [Fact]
    public async Task The_list_says_what_is_true_of_each_library_here()
    {
        (LibraryService service, _) = Fixture();
        var dialog = new LibraryDialogViewModel(service, () => { });

        await dialog.LoadAsync();

        Assert.Contains("4", dialog.Summary, StringComparison.Ordinal);
        Assert.False(dialog.IsBusy);
        LibraryRow gadget = Row(dialog, "Gadget");
        Assert.True(gadget.CanInstall);
        Assert.Equal("2.0.0", gadget.SelectedVersion);
        Assert.Equal(["2.0.0", "1.0.0"], gadget.Versions);
        Assert.Contains("Helper", gadget.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("LiquidCrystal", gadget.Note, StringComparison.Ordinal);

        LibraryRow bundled = Row(dialog, "LiquidCrystal");
        Assert.False(bundled.CanInstall);
        Assert.Contains("Встроена", bundled.Status, StringComparison.Ordinal);

        Assert.Contains("samd", Row(dialog, "SamdOnly").Note, StringComparison.Ordinal);
        Assert.True(Row(dialog, "SamdOnly").CanInstall);
    }

    [Fact]
    public async Task Installing_brings_its_dependencies_and_reports_and_the_row_follows_the_version()
    {
        (LibraryService service, string project) = Fixture();
        int changes = 0;
        var dialog = new LibraryDialogViewModel(service, () => changes++);
        await dialog.LoadAsync();
        LibraryRow gadget = Row(dialog, "Gadget");
        gadget.SelectedVersion = "1.0.0";

        dialog.InstallCommand.Execute(gadget);
        await WaitUntilIdle(dialog);

        Assert.Equal("", dialog.Error);
        Assert.True(File.Exists(Path.Combine(project, "libs", "Gadget", "src", "Gadget.h")));
        Assert.True(File.Exists(Path.Combine(project, "libs", "Helper", "src", "Helper.h")));
        Assert.Equal(1, changes);
        Assert.Contains("Gadget, Helper", dialog.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.0", gadget.Status, StringComparison.Ordinal);
        Assert.False(gadget.CanInstall);
        Assert.True(gadget.CanRemove);

        gadget.SelectedVersion = "2.0.0";
        Assert.True(gadget.CanInstall);
        Assert.Equal("Обновить", gadget.ActionLabel);

        dialog.InstallCommand.Execute(gadget);
        await WaitUntilIdle(dialog);
        Assert.Contains("2.0.0", gadget.Status, StringComparison.Ordinal);
        Assert.False(gadget.CanInstall);

        gadget.SelectedVersion = "1.0.0";
        Assert.Equal("Заменить", gadget.ActionLabel);

        dialog.RemoveCommand.Execute(gadget);
        Assert.False(Directory.Exists(Path.Combine(project, "libs", "Gadget")));
        Assert.False(gadget.CanRemove);
    }

    private static async Task WaitUntilIdle(LibraryDialogViewModel dialog)
    {
        for (int i = 0; i < 500 && dialog.IsBusy; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.False(dialog.IsBusy);
    }

    [Fact]
    public async Task A_query_given_at_the_start_is_searched_and_a_header_finds_its_library()
    {
        (LibraryService service, _) = Fixture();
        var dialog = new LibraryDialogViewModel(service, () => { }, "Gadget.h");

        await dialog.LoadAsync();

        Assert.Equal(["Gadget"], dialog.Rows.Select(r => r.Name));
        dialog.Query = "nothing like this";
        Assert.Empty(dialog.Rows);
        Assert.NotEqual("", dialog.ListNote);
    }

    [Fact]
    public async Task With_no_catalog_the_dialog_says_so_and_a_zip_still_works()
    {
        (LibraryService service, string project) = Fixture();
        _served.Clear();
        var dialog = new LibraryDialogViewModel(service, () => { });

        await dialog.LoadAsync();
        Assert.Contains("arduino.cc", dialog.Error, StringComparison.Ordinal);
        Assert.Empty(dialog.Rows);

        string zip = Path.Combine(_root, "Thing-main.zip");
        await File.WriteAllBytesAsync(zip, Zip(("Thing-main/Thing.h", "// thing\n")), TestContext.Current.CancellationToken);
        await dialog.AddPathAsync(zip);

        Assert.Equal("", dialog.Error);
        Assert.True(File.Exists(Path.Combine(project, "libs", "Thing", "Thing.h")));
        Assert.Contains("Thing", dialog.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_folder_with_no_library_says_what_a_library_is()
    {
        (LibraryService service, _) = Fixture();
        var dialog = new LibraryDialogViewModel(service, () => { });
        string empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        await dialog.AddPathAsync(empty);

        Assert.Contains("library.properties", dialog.Error, StringComparison.Ordinal);
        Assert.Equal("", dialog.Message);
    }

    private static void OnSta(Func<Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(Run);
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            async Task Run()
            {
                try
                {
                    await body();
                }
                catch (Exception e)
                {
                    failure = e;
                }

                frame.Continue = false;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private MainViewModel NewViewModel() =>
        new(new AppEnvironment(false), new FirmwareBuildService(), new UserState(Path.Combine(_root, "ui-state.json"), NullLogger<UserState>.Instance), NullLogger<MainViewModel>.Instance);

    private string ProjectWithLibrary()
    {
        string project = Path.Combine(_root, "withlib");
        Directory.CreateDirectory(project);
        ProjectTemplate.Create(project, Nano.Id);
        string library = Path.Combine(project, "libs", "My_Display");
        Directory.CreateDirectory(Path.Combine(library, "src"));
        File.WriteAllText(Path.Combine(library, "library.properties"), "name=My Display\nversion=1.2\n");
        File.WriteAllText(Path.Combine(library, "src", "My_Display.h"), "// header\n");
        return project;
    }

    [Fact]
    public void The_explorer_lists_the_projects_libraries_and_removes_them_to_the_bin() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        var recycled = new List<string>();
        vm.RecycleFolder = path =>
        {
            recycled.Add(path);
            Directory.Delete(path, recursive: true);
        };
        string project = ProjectWithLibrary();
        Assert.True(await vm.OpenAsync(project));
        vm.LeftTab = MainViewModel.ProjectPage;

        ExplorerGroup group = vm.ExplorerGroups.Single(g => g.Key == "libraries");
        Assert.True(group.CanAddLibrary);
        ExplorerEntry row = group.Items.Single();
        Assert.Equal("My Display", row.Title);
        Assert.Equal("в проекте", row.Detail);
        Assert.True(row.CanRemove);
        Assert.Contains("1.2", row.Tip, StringComparison.Ordinal);

        vm.OpenExplorerEntryCommand.Execute(row);
        Assert.EndsWith("My_Display.h", Assert.IsType<CodeDocumentViewModel>(vm.SelectedTab).Path, StringComparison.Ordinal);

        vm.RemoveLibraryCommand.Execute(row);
        Assert.Equal([row.Path!], recycled);
        Assert.Equal(ExplorerKind.Note, vm.ExplorerGroups.Single(g => g.Key == "libraries").Items.Single().Kind);
        await vm.DisposeAsync();
    });

    [Fact]
    public void The_sketchs_list_does_not_show_the_libraries_when_they_share_a_folder() => OnSta(async () =>
    {
        string project = ProjectWithLibrary();
        string sketch = Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(project, "sketch"), "*.ino").Single())!;
        string file = Path.Combine(project, ProjectFile.FileName);
        ProjectFile loaded = ProjectFile.Load(project);
        foreach (string ino in Directory.EnumerateFiles(sketch, "*.ino"))
        {
            File.Move(ino, Path.Combine(project, Path.GetFileName(ino)));
        }

        (loaded with { SketchPath = "." }).Save(project);
        Assert.True(File.Exists(file));

        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(project));
        vm.LeftTab = MainViewModel.ProjectPage;

        Assert.DoesNotContain(vm.ExplorerGroups.Single(g => g.Key == "sketch").Items, r => r.Title.Contains("My_Display", StringComparison.Ordinal));
        await vm.DisposeAsync();
    });

    [Fact]
    public void The_project_properties_no_longer_ask_for_paths() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(ProjectWithLibrary()));

        Assert.DoesNotContain(vm.Properties!.Fields, f => f.Name == "libraryPaths");
        await vm.DisposeAsync();
    });

    [Fact]
    public void A_missing_header_offers_the_dialog_with_it_searched() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(ProjectWithLibrary()));
        LibraryDialogViewModel? shown = null;
        vm.ShowLibraries = dialog => shown = dialog;

        var diagnostic = new BuildDiagnostic(DiagnosticSeverity.Error, "'Gadget.h' file not found", "a.ino", 1, 10);
        DiagnosticFix fix = DiagnosticHints.FixFor(diagnostic)!;
        Assert.Equal("Gadget.h", fix.FindLibrary);
        Assert.NotNull(DiagnosticHints.Explain(diagnostic));

        vm.AddLibraryCommand.Execute("Gadget.h");
        Assert.Equal("Gadget.h", shown?.Query);
        await vm.DisposeAsync();
    });
}
