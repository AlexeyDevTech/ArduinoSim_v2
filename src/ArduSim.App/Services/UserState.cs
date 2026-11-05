using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ArduSim.App.Services;

public sealed record RecentProject(string Name, string Path);

/// <summary>Panel sizes and visibility (docs/UI.md §2.1).</summary>
public sealed record PanelLayout
{
    public double PaletteWidth { get; init; } = 240;

    public double PropertiesWidth { get; init; } = 280;

    public double BottomHeight { get; init; } = 240;

    public bool PaletteOpen { get; init; } = true;

    public bool PropertiesOpen { get; init; } = true;

    public bool BottomOpen { get; init; } = true;

    public int BottomTab { get; init; }
}

/// <summary>
/// Per-user app state that is not part of any project: recent projects, the panel layout and the theme,
/// in <c>%LOCALAPPDATA%\ArduSim\ui-state.json</c>. A missing or unreadable file means defaults.
/// </summary>
public sealed partial class UserState
{
    public const int RecentLimit = 10;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly ILogger<UserState> _logger;
    private Data _data;

    public UserState(ILogger<UserState> logger)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArduSim", "ui-state.json"), logger)
    {
    }

    public UserState(string path, ILogger<UserState> logger)
    {
        _path = path;
        _logger = logger;
        _data = Load();
    }

    public IReadOnlyList<RecentProject> Recent => _data.Recent;

    public PanelLayout Layout
    {
        get => _data.Layout;
        set
        {
            _data = _data with { Layout = value };
            Save();
        }
    }

    /// <summary>The color theme, <c>dark</c> (default) or <c>light</c>.</summary>
    public string Theme
    {
        get => _data.Theme;
        set
        {
            _data = _data with { Theme = value };
            Save();
        }
    }

    /// <summary>The buzzers sound (the toolbar's speaker button).</summary>
    public bool Sound
    {
        get => _data.Sound;
        set
        {
            _data = _data with { Sound = value };
            Save();
        }
    }

    /// <summary>Moves (or adds) a project to the top of the recent list.</summary>
    public void Touch(string directory)
    {
        string full = Path.GetFullPath(directory);
        List<RecentProject> recent = [new(Path.GetFileName(full), full), .. _data.Recent.Where(r => !string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase))];
        _data = _data with { Recent = [.. recent.Take(RecentLimit)] };
        Save();
    }

    public void Forget(string directory)
    {
        _data = _data with { Recent = [.. _data.Recent.Where(r => !string.Equals(r.Path, directory, StringComparison.OrdinalIgnoreCase))] };
        Save();
    }

    private Data Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path), Options) ?? new Data() : new Data();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            LogUnreadable(_logger, _path, e.Message);
            return new Data();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogUnwritable(_logger, _path, e.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring unreadable {Path}: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write {Path}: {Reason}")]
    private static partial void LogUnwritable(ILogger logger, string path, string reason);

    private sealed record Data
    {
        public IReadOnlyList<RecentProject> Recent { get; init; } = [];

        public PanelLayout Layout { get; init; } = new();

        public string Theme { get; init; } = "dark";

        public bool Sound { get; init; } = true;
    }
}
