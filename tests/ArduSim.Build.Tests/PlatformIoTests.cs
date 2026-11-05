using ArduSim.Build.Import;
using ArduSim.Build.Sketches;

namespace ArduSim.Build.Tests;

/// <summary>Reading <c>platformio.ini</c> (ADR-015).</summary>
public sealed class PlatformIoTests
{
    private const string Typical = """
        ; a typical project
        [platformio]
        default_envs = release

        [env]
        framework = arduino
        monitor_speed = 115200

        [common]
        flags = -DCOMMON=1

        [env:debug]
        platform = atmelavr
        board = uno

        [env:release]
        platform = atmelavr
        board = nanoatmega328new
        build_flags =
            ${common.flags}
            -D RELEASE
            -DLEVEL=3
            -Iconfig
        lib_deps =
            adafruit/Adafruit SSD1306 @ ^2.5.7
            paulstoffregen/OneWire@2.3.8
            https://github.com/someone/CustomThing.git
            Wire
        """;

    [Fact]
    public void Takes_the_default_environment_with_its_flags_and_libraries()
    {
        PlatformIoProject project = PlatformIoProject.Parse(Typical);

        Assert.Equal("release", project.Environment);
        Assert.Equal("arduino-nano-v3", project.BoardId);
        Assert.Equal("src", project.SourceDirectory);
        Assert.Equal(["COMMON=1", "RELEASE", "LEVEL=3"], project.Defines);
        Assert.Equal(["include", "config"], project.IncludeDirectories);
        Assert.Equal(["lib"], project.LibraryDirectories);
        Assert.Equal(["Adafruit SSD1306", "OneWire", "CustomThing", "Wire"], project.LibraryDependencies);
        Assert.Equal(115200, project.MonitorSpeed);
    }

    [Fact]
    public void Without_default_envs_the_first_environment_counts_and_extends_inherits()
    {
        PlatformIoProject project = PlatformIoProject.Parse("""
            [env:base]
            framework = arduino
            board = esp32dev
            build_flags = -DBASE

            [env:child]
            extends = env:base
            """);

        Assert.Equal("base", project.Environment);
        Assert.Equal("esp32-devkit-v1", project.BoardId);

        PlatformIoProject child = PlatformIoProject.Parse("""
            [platformio]
            default_envs = child

            [env:base]
            framework = arduino
            board = d1_mini
            monitor_speed = 74880

            [env:child]
            extends = env:base
            build_flags = -DCHILD
            """);
        Assert.Equal("wemos-d1-mini", child.BoardId);
        Assert.Equal(["CHILD"], child.Defines);
        Assert.Equal(74880, child.MonitorSpeed);
    }

    [Fact]
    public void String_macros_lose_their_escapes_and_may_hold_spaces()
    {
        PlatformIoProject project = PlatformIoProject.Parse("""
            [env:a]
            framework = arduino
            board = uno
            build_flags = -DNAME=\"hello world\" -D "QUOTED=1" -DPLAIN
            """);

        Assert.Equal(["NAME=\"hello world\"", "QUOTED=1", "PLAIN"], project.Defines);
    }

    [Theory]
    [InlineData("uno", "arduino-uno-r3")]
    [InlineData("megaatmega2560", "arduino-mega-2560")]
    [InlineData("pro16MHzatmega328", "arduino-pro-mini-5v")]
    [InlineData("pro8MHzatmega328", "arduino-pro-mini-3v3")]
    [InlineData("nodemcuv2", "nodemcu-v3")]
    public void Boards_map_to_their_profiles(string board, string expected)
    {
        Assert.Equal(expected, PlatformIoProject.Parse($"[env:a]\nframework = arduino\nboard = {board}\n").BoardId);
    }

    [Theory]
    [InlineData("[platformio]\nsrc_dir = code\n", "no [env:")]
    [InlineData("[env:a]\nframework = espidf\nboard = esp32dev\n", "Arduino framework")]
    [InlineData("[env:a]\nframework = arduino\nboard = teensy41\n", "teensy41")]
    [InlineData("[platformio]\ndefault_envs = b\n[env:a]\nframework = arduino\nboard = uno\n", "[env:b]")]
    public void What_cannot_run_is_refused_with_the_reason(string text, string reason)
    {
        var e = Assert.Throws<PlatformIoException>(() => PlatformIoProject.Parse(text));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_source_and_downloaded_library_folders_are_found_and_missing_libraries_named()
    {
        string root = Directory.CreateTempSubdirectory("ardusim-pio-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".pio", "libdeps", "a"));
            string library = Path.Combine(root, "libs", "Adafruit_GFX");
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(library, "library.properties"), "name=Adafruit GFX Library\nversion=1.0\n");
            File.WriteAllText(Path.Combine(root, PlatformIoProject.FileName), """
                [platformio]
                src_dir = code
                lib_dir = libs

                [env:a]
                framework = arduino
                board = uno
                lib_deps =
                    adafruit/Adafruit GFX Library
                    Nowhere Library
                """);

            Assert.True(PlatformIoProject.ExistsIn(root));
            PlatformIoProject project = PlatformIoProject.Read(root);

            Assert.Equal("code", project.SourceDirectory);
            Assert.Equal(["libs", Path.Combine(".pio", "libdeps", "a")], project.LibraryDirectories);
            Assert.Equal(["Nowhere Library"], project.MissingLibraries([Path.Combine(root, "libs")]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_folder_without_ino_is_a_plain_cpp_project_with_every_source_under_it()
    {
        string root = Directory.CreateTempSubdirectory("ardusim-pio-").FullName;
        try
        {
            Assert.Throws<SketchException>(() => Sketch.Load(root));

            Directory.CreateDirectory(Path.Combine(root, "drivers"));
            File.WriteAllText(Path.Combine(root, "main.cpp"), "");
            File.WriteAllText(Path.Combine(root, "drivers", "motor.cpp"), "");

            Sketch sketch = Sketch.Load(root);

            Assert.Empty(sketch.InoFiles);
            Assert.Equal(2, sketch.SourceFiles.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
