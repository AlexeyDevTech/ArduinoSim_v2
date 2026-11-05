using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ArduSim.App.Services;

public sealed record CompletionEntry(string Label, string InsertText, string Detail, string Documentation, int Kind, string? SortText);

public sealed record SignatureEntry(string Label, string Documentation);

public sealed record SymbolEntry(string Name, string Detail, int Line, int Kind);

public sealed record SourceLocationEntry(string Path, int Line, int Column);

/// <summary>
/// The compiler's language server (clangd, part of the pinned toolchain, ADR-016): completion, signature
/// help, hover, go to definition and the file's outline over LSP. It only reads: the build's own
/// diagnostics stay the ones shown. Every call may answer null: without clangd the editor still edits.
/// </summary>
public sealed partial class ClangdClient : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    // The outline waits for the file's headers to be read, which can take a while the first time.
    private static readonly TimeSpan OutlineTimeout = TimeSpan.FromSeconds(45);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly Dictionary<string, int> _versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Stream _input;
    private int _nextId;

    private ClangdClient(Process process, ILogger logger)
    {
        _process = process;
        _logger = logger;
        _input = process.StandardInput.BaseStream;
        _ = Task.Run(ReadLoopAsync);
    }

    /// <summary>Starts clangd for a project; null when it cannot start (the editor then just edits).</summary>
    public static async Task<ClangdClient?> StartAsync(string clangdPath, string projectDirectory, IReadOnlyList<string> flags, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(flags);
        if (!File.Exists(clangdPath))
        {
            return null;
        }

        var start = new ProcessStartInfo(clangdPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = projectDirectory,
            StandardOutputEncoding = Encoding.Latin1,
        };
        foreach (string argument in (string[])["--log=error", "--background-index=false", "--header-insertion=never", "--pch-storage=memory", "--completion-style=detailed", "--function-arg-placeholders=0", "--clang-tidy=false"])
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            var client = new ClangdClient(process, logger);
            var parameters = new JsonObject
            {
                ["processId"] = Environment.ProcessId,
                ["rootUri"] = ToUri(projectDirectory),
                ["capabilities"] = new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["completion"] = new JsonObject { ["completionItem"] = new JsonObject { ["snippetSupport"] = false, ["documentationFormat"] = new JsonArray("plaintext") } },
                        ["hover"] = new JsonObject { ["contentFormat"] = new JsonArray("plaintext") },
                        ["signatureHelp"] = new JsonObject { ["signatureInformation"] = new JsonObject { ["documentationFormat"] = new JsonArray("plaintext") } },
                        ["documentSymbol"] = new JsonObject { ["hierarchicalDocumentSymbolSupport"] = true },
                    },
                },
                ["initializationOptions"] = new JsonObject { ["fallbackFlags"] = new JsonArray([.. flags.Select(f => (JsonNode?)JsonValue.Create(f))]) },
            };
            if (await client.RequestAsync("initialize", parameters) is null)
            {
                await client.DisposeAsync();
                return null;
            }

            await client.NotifyAsync("initialized", new JsonObject());
            return client;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            LogStartFailed(logger, e.Message);
            return null;
        }
    }

    private static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static string FromUri(string uri) => new Uri(uri).LocalPath;

    /// <summary>Sends the editor's text; clangd sees every file as C++, the .ino sketch too.</summary>
    /// <summary>Whether this server has been told about the file; a new server after a board change has not.</summary>
    public bool Knows(string path) => _versions.ContainsKey(path);

    public async Task UpdateAsync(string path, string text)
    {
        string uri = ToUri(path);
        if (_versions.TryGetValue(path, out int version))
        {
            _versions[path] = ++version;
            await NotifyAsync("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
            });
        }
        else
        {
            _versions[path] = 1;
            await NotifyAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "cpp", ["version"] = 1, ["text"] = text },
            });
        }
    }

    public async Task<IReadOnlyList<CompletionEntry>> CompleteAsync(string path, int line, int column)
    {
        JsonElement? result = await RequestAsync("textDocument/completion", Position(path, line, column));
        if (result is not { } answer)
        {
            return [];
        }

        JsonElement items = answer.ValueKind == JsonValueKind.Array ? answer : answer.TryGetProperty("items", out JsonElement list) ? list : default;
        if (items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var entries = new List<CompletionEntry>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            string label = Text(item, "label").Trim();
            string insert = item.TryGetProperty("textEdit", out JsonElement edit) ? Text(edit, "newText") : Text(item, "insertText");
            entries.Add(new CompletionEntry(
                label,
                insert.Length > 0 ? insert : label,
                Text(item, "detail"),
                item.TryGetProperty("documentation", out JsonElement doc) ? (doc.ValueKind == JsonValueKind.String ? doc.GetString() ?? "" : Text(doc, "value")) : "",
                item.TryGetProperty("kind", out JsonElement kind) ? kind.GetInt32() : 0,
                item.TryGetProperty("sortText", out JsonElement sort) ? sort.GetString() : null));
        }

        return entries;
    }

    public async Task<IReadOnlyList<SignatureEntry>> SignaturesAsync(string path, int line, int column)
    {
        JsonElement? result = await RequestAsync("textDocument/signatureHelp", Position(path, line, column));
        if (result is not { ValueKind: JsonValueKind.Object } answer || !answer.TryGetProperty("signatures", out JsonElement signatures))
        {
            return [];
        }

        return [.. signatures.EnumerateArray().Select(s => new SignatureEntry(
            Text(s, "label"),
            s.TryGetProperty("documentation", out JsonElement d) ? (d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : Text(d, "value")) : ""))];
    }

    public async Task<string?> HoverAsync(string path, int line, int column)
    {
        JsonElement? result = await RequestAsync("textDocument/hover", Position(path, line, column));
        if (result is not { ValueKind: JsonValueKind.Object } answer || !answer.TryGetProperty("contents", out JsonElement contents))
        {
            return null;
        }

        string text = contents.ValueKind == JsonValueKind.String ? contents.GetString() ?? "" : Text(contents, "value");
        return text.Length == 0 ? null : text.Trim();
    }

    public async Task<SourceLocationEntry?> DefinitionAsync(string path, int line, int column)
    {
        JsonElement? result = await RequestAsync("textDocument/definition", Position(path, line, column));
        if (result is not { } answer)
        {
            return null;
        }

        JsonElement location = answer.ValueKind == JsonValueKind.Array ? (answer.GetArrayLength() > 0 ? answer[0] : default) : answer;
        if (location.ValueKind != JsonValueKind.Object || !location.TryGetProperty("range", out JsonElement range))
        {
            return null;
        }

        JsonElement start = range.GetProperty("start");
        return new SourceLocationEntry(FromUri(Text(location, "uri")), start.GetProperty("line").GetInt32() + 1, start.GetProperty("character").GetInt32() + 1);
    }

    /// <summary>The file's functions, types and variables, for the explorer's Структура group.</summary>
    public async Task<IReadOnlyList<SymbolEntry>> SymbolsAsync(string path)
    {
        JsonElement? result = await RequestAsync("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = ToUri(path) } }, OutlineTimeout);
        if (result is not { ValueKind: JsonValueKind.Array } answer)
        {
            return [];
        }

        var symbols = new List<SymbolEntry>();
        foreach (JsonElement symbol in answer.EnumerateArray())
        {
            int line = symbol.TryGetProperty("range", out JsonElement range) ? range.GetProperty("start").GetProperty("line").GetInt32() + 1 : 1;
            symbols.Add(new SymbolEntry(Text(symbol, "name"), Text(symbol, "detail"), line, symbol.TryGetProperty("kind", out JsonElement kind) ? kind.GetInt32() : 0));
        }

        return symbols;
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static JsonObject Position(string path, int line, int column) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = ToUri(path) },
        ["position"] = new JsonObject { ["line"] = line - 1, ["character"] = column - 1 },
    };

    private async Task<JsonElement?> RequestAsync(string method, JsonObject parameters, TimeSpan? timeout = null)
    {
        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            return await completion.Task.WaitAsync(timeout ?? RequestTimeout, _stop.Token);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task NotifyAsync(string method, JsonObject parameters)
    {
        try
        {
            await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            LogWriteFailed(_logger, e.Message);
        }
    }

    private async Task SendAsync(JsonObject message)
    {
        byte[] body = Utf8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _writeLock.WaitAsync(_stop.Token);
        try
        {
            await _input.WriteAsync(header, _stop.Token);
            await _input.WriteAsync(body, _stop.Token);
            await _input.FlushAsync(_stop.Token);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        Stream output = _process.StandardOutput.BaseStream;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                int length = await ReadHeaderAsync(output);
                if (length <= 0)
                {
                    break;
                }

                byte[] body = new byte[length];
                await output.ReadExactlyAsync(body, _stop.Token);
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number && _pending.TryRemove(id.GetInt32(), out TaskCompletionSource<JsonElement?>? waiting))
                {
                    waiting.TrySetResult(root.TryGetProperty("result", out JsonElement result) && result.ValueKind != JsonValueKind.Null ? result.Clone() : null);
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or OperationCanceledException or ObjectDisposedException or EndOfStreamException)
        {
            LogReadEnded(_logger, e.Message);
        }

        foreach (TaskCompletionSource<JsonElement?> waiting in _pending.Values)
        {
            waiting.TrySetResult(null);
        }
    }

    private async Task<int> ReadHeaderAsync(Stream output)
    {
        var line = new StringBuilder();
        int length = 0;
        byte[] one = new byte[1];
        while (true)
        {
            int read = await output.ReadAsync(one, _stop.Token);
            if (read == 0)
            {
                return 0;
            }

            if (one[0] != '\n')
            {
                line.Append((char)one[0]);
                continue;
            }

            string text = line.ToString().Trim();
            line.Clear();
            if (text.Length == 0)
            {
                return length;
            }

            if (text.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(text.AsSpan(15).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            LogStopFailed(_logger, e.Message);
        }

        _process.Dispose();
        _stop.Dispose();
        _writeLock.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "clangd did not start: {Reason}")]
    private static partial void LogStartFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Writing to clangd failed: {Reason}")]
    private static partial void LogWriteFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "clangd's output ended: {Reason}")]
    private static partial void LogReadEnded(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stopping clangd failed: {Reason}")]
    private static partial void LogStopFailed(ILogger logger, string reason);
}
