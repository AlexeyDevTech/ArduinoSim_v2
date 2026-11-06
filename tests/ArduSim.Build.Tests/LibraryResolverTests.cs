using ArduSim.Build.Libraries;

namespace ArduSim.Build.Tests;

public sealed class LibraryResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ardusim-libs-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, string content = "")
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Dir(string relative) => Path.GetFullPath(Path.Combine(_root, relative));

    [Fact]
    public void Resolves_a_recursive_layout_library_and_its_dependencies()
    {
        Write("bundled/Gfx/library.properties", "name=Gfx\n");
        Write("bundled/Gfx/src/Gfx.h", "#include <Bus.h>\n");
        Write("bundled/Gfx/src/Gfx.cpp");
        Write("bundled/Gfx/src/fonts/Font.cpp");
        Write("bundled/Bus/Bus.h");
        Write("bundled/Bus/Bus.cpp");
        Write("bundled/Bus/utility/twi.c");
        string sketch = Write("sketch/S/S.ino", "#include <Gfx.h>\n#include <stdint.h>\n");

        LibraryResolution result = new LibraryResolver([Dir("bundled")], []).Resolve([sketch]);

        Assert.Equal(["Gfx", "Bus"], result.Libraries.Select(l => l.Name));
        Assert.Equal(LibraryLayout.Recursive, result.Libraries[0].Layout);
        Assert.Equal(2, result.Libraries[0].SourceFiles().Count);
        Assert.Equal(LibraryLayout.Flat, result.Libraries[1].Layout);
        Assert.Contains(Dir("bundled/Bus/utility"), result.Libraries[1].IncludeDirectories);
        Assert.Equal(2, result.Libraries[1].SourceFiles().Count);
        Assert.Equal(["stdint.h"], result.UnresolvedIncludes);
    }

    [Fact]
    public void What_a_sketchs_own_header_includes_is_found_too()
    {
        Write("bundled/Bus/Bus.h");
        Write("bundled/Oled/Oled.h");
        Write("sketch/S/src/App.h", "#include <Bus.h>\n#include \"more/Deep.h\"\n");
        Write("sketch/S/src/more/Deep.h", "#include <Oled.h>\n");
        string sketch = Write("sketch/S/S.ino", "#include \"src/App.h\"\n");

        LibraryResolution result = new LibraryResolver([Dir("bundled")], []).Resolve([sketch]);

        Assert.Equal(["Bus", "Oled"], result.Libraries.Select(l => l.Name));
        Assert.Empty(result.UnresolvedIncludes);
    }

    [Fact]
    public void A_header_that_includes_itself_ends()
    {
        Write("sketch/S/a.h", "#include \"b.h\"\n");
        Write("sketch/S/b.h", "#include \"a.h\"\n");
        string sketch = Write("sketch/S/S.ino", "#include \"a.h\"\n");

        Assert.Empty(new LibraryResolver([Dir("sketch")], []).Resolve([sketch]).UnresolvedIncludes);
    }

    [Fact]
    public void Earlier_search_roots_win()
    {
        Write("documents/Oled/Oled.h");
        Write("bundled/Oled/Oled.h");
        string sketch = Write("sketch/S/S.ino", "#include \"Oled.h\"\n");

        LibraryResolution result = new LibraryResolver([Dir("documents"), Dir("bundled")], []).Resolve([sketch]);

        Assert.Equal(Dir("documents/Oled"), Assert.Single(result.Libraries).Directory);
    }

    [Fact]
    public void Within_a_root_the_folder_named_after_the_header_wins()
    {
        Write("bundled/AaaFork/Display.h");
        Write("bundled/Display/Display.h");
        string sketch = Write("sketch/S/S.ino", "#include <Display.h>\n");

        LibraryResolution result = new LibraryResolver([Dir("bundled")], []).Resolve([sketch]);

        Assert.Equal("Display", Assert.Single(result.Libraries).Name);
    }

    [Fact]
    public void Sketch_local_and_core_headers_do_not_pull_libraries()
    {
        Write("core/include/Arduino.h");
        Write("bundled/Arduino/Arduino.h");
        Write("bundled/config/config.h");
        Write("sketch/S/config.h");
        string sketch = Write("sketch/S/S.ino", "#include <Arduino.h>\n#include \"config.h\"\n");

        LibraryResolution result = new LibraryResolver([Dir("bundled")], [Dir("core/include")]).Resolve([sketch]);

        Assert.Empty(result.Libraries);
        Assert.Empty(result.UnresolvedIncludes);
    }

    [Fact]
    public void Commented_out_includes_are_ignored()
    {
        Write("bundled/Unused/Unused.h");
        string sketch = Write("sketch/S/S.ino", "// #include <Unused.h>\n/*\n#include <Unused.h>\n*/\n");

        Assert.Empty(new LibraryResolver([Dir("bundled")], []).Resolve([sketch]).Libraries);
    }
}
