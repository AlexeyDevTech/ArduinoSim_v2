using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArduSim.Core.Simulation;

/// <summary>Value types of the ESP Preferences store, as <c>SimKvType</c> in sim_abi.h numbers them.</summary>
public enum KvType
{
    I8 = 1,
    U8,
    I16,
    U16,
    I32,
    U32,
    I64,
    U64,
    Real32,
    Real64,
    Bool,
    Text,
    Bytes,
}

/// <param name="Value">The raw little-endian bytes as the firmware stored them (UTF-8 for strings).</param>
public sealed record KvEntry(KvType Type, byte[] Value);

/// <summary>
/// The ESP <c>Preferences</c> store (ADR-009): namespaces of typed values. The file-backed variant
/// keeps the project's <c>preferences.json</c> and rewrites it after every change, so values
/// survive a Stop or a crash; entries that cannot be read are skipped and counted in
/// <see cref="SkippedEntries"/>.
/// </summary>
public sealed class KeyValueStore
{
    /// <summary>NVS limits namespace and key names to 15 bytes.</summary>
    public const int MaxNameLength = 15;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, SortedDictionary<string, KvEntry>> _spaces = new(StringComparer.Ordinal);
    private readonly string? _path;

    private KeyValueStore(string? path) => _path = path;

    public int SkippedEntries { get; private set; }

    public static KeyValueStore InMemory() => new(null);

    public static KeyValueStore FileBacked(string path)
    {
        var store = new KeyValueStore(path);
        store.Load();
        return store;
    }

    public static bool IsValidName(string name) => name.Length > 0 && Encoding.UTF8.GetByteCount(name) <= MaxNameLength;

    public KvEntry? Get(string nameSpace, string key) =>
        _spaces.TryGetValue(nameSpace, out var entries) && entries.TryGetValue(key, out KvEntry? entry) ? entry : null;

    public int Count(string nameSpace) => _spaces.TryGetValue(nameSpace, out var entries) ? entries.Count : 0;

    public void Put(string nameSpace, string key, KvType type, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_spaces.TryGetValue(nameSpace, out var entries))
        {
            entries = new SortedDictionary<string, KvEntry>(StringComparer.Ordinal);
            _spaces[nameSpace] = entries;
        }

        entries[key] = new KvEntry(type, value);
        Save();
    }

    public bool Remove(string nameSpace, string key)
    {
        bool removed = _spaces.TryGetValue(nameSpace, out var entries) && entries.Remove(key);
        if (removed)
        {
            Save();
        }

        return removed;
    }

    public void Clear(string nameSpace)
    {
        if (_spaces.Remove(nameSpace))
        {
            Save();
        }
    }

    private static JsonValue? ToJson(KvEntry entry)
    {
        byte[] v = entry.Value;
        return entry.Type switch
        {
            KvType.I8 when v.Length >= 1 => JsonValue.Create((sbyte)v[0]),
            KvType.U8 when v.Length >= 1 => JsonValue.Create(v[0]),
            KvType.I16 when v.Length >= 2 => JsonValue.Create(BinaryPrimitives.ReadInt16LittleEndian(v)),
            KvType.U16 when v.Length >= 2 => JsonValue.Create(BinaryPrimitives.ReadUInt16LittleEndian(v)),
            KvType.I32 when v.Length >= 4 => JsonValue.Create(BinaryPrimitives.ReadInt32LittleEndian(v)),
            KvType.U32 when v.Length >= 4 => JsonValue.Create(BinaryPrimitives.ReadUInt32LittleEndian(v)),
            KvType.I64 when v.Length >= 8 => JsonValue.Create(BinaryPrimitives.ReadInt64LittleEndian(v)),
            KvType.U64 when v.Length >= 8 => JsonValue.Create(BinaryPrimitives.ReadUInt64LittleEndian(v)),
            KvType.Real32 when v.Length >= 4 => JsonValue.Create(BinaryPrimitives.ReadSingleLittleEndian(v)),
            KvType.Real64 when v.Length >= 8 => JsonValue.Create(BinaryPrimitives.ReadDoubleLittleEndian(v)),
            KvType.Bool when v.Length >= 1 => JsonValue.Create(v[0] != 0),
            KvType.Text => JsonValue.Create(Encoding.UTF8.GetString(v)),
            _ => JsonValue.Create(Convert.ToBase64String(v)),
        };
    }

    private static byte[]? FromJson(KvType type, JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        try
        {
            byte[] bytes = new byte[type switch
            {
                KvType.I8 or KvType.U8 or KvType.Bool => 1,
                KvType.I16 or KvType.U16 => 2,
                KvType.I32 or KvType.U32 or KvType.Real32 => 4,
                KvType.I64 or KvType.U64 or KvType.Real64 => 8,
                _ => 0,
            }];
            switch (type)
            {
                case KvType.I8: bytes[0] = (byte)value.GetValue<sbyte>(); break;
                case KvType.U8: bytes[0] = value.GetValue<byte>(); break;
                case KvType.Bool: bytes[0] = value.GetValue<bool>() ? (byte)1 : (byte)0; break;
                case KvType.I16: BinaryPrimitives.WriteInt16LittleEndian(bytes, value.GetValue<short>()); break;
                case KvType.U16: BinaryPrimitives.WriteUInt16LittleEndian(bytes, value.GetValue<ushort>()); break;
                case KvType.I32: BinaryPrimitives.WriteInt32LittleEndian(bytes, value.GetValue<int>()); break;
                case KvType.U32: BinaryPrimitives.WriteUInt32LittleEndian(bytes, value.GetValue<uint>()); break;
                case KvType.I64: BinaryPrimitives.WriteInt64LittleEndian(bytes, value.GetValue<long>()); break;
                case KvType.U64: BinaryPrimitives.WriteUInt64LittleEndian(bytes, value.GetValue<ulong>()); break;
                case KvType.Real32: BinaryPrimitives.WriteSingleLittleEndian(bytes, value.GetValue<float>()); break;
                case KvType.Real64: BinaryPrimitives.WriteDoubleLittleEndian(bytes, value.GetValue<double>()); break;
                case KvType.Text: return Encoding.UTF8.GetBytes(value.GetValue<string>());
                default: return Convert.FromBase64String(value.GetValue<string>());
            }

            return bytes;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static readonly Dictionary<KvType, string> TypeNames = new()
    {
        [KvType.I8] = "i8", [KvType.U8] = "u8", [KvType.I16] = "i16", [KvType.U16] = "u16", [KvType.I32] = "i32", [KvType.U32] = "u32",
        [KvType.I64] = "i64", [KvType.U64] = "u64", [KvType.Real32] = "float", [KvType.Real64] = "double", [KvType.Bool] = "bool",
        [KvType.Text] = "string", [KvType.Bytes] = "bytes",
    };

    private static string TypeName(KvType type) => TypeNames[type];

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            SkippedEntries++;
            return;
        }

        foreach ((string nameSpace, JsonNode? entries) in root as JsonObject ?? [])
        {
            foreach ((string key, JsonNode? item) in entries as JsonObject ?? [])
            {
                string? typeName = (item?["type"] as JsonValue)?.TryGetValue(out string? name) == true ? name : null;
                if (typeName is null || TypeNames.FirstOrDefault(p => p.Value == typeName).Key is not (> 0 and var type)
                    || FromJson(type, item!["value"]) is not { } bytes)
                {
                    SkippedEntries++;
                    continue;
                }

                if (!_spaces.TryGetValue(nameSpace, out var space))
                {
                    space = new SortedDictionary<string, KvEntry>(StringComparer.Ordinal);
                    _spaces[nameSpace] = space;
                }

                space[key] = new KvEntry(type, bytes);
            }
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        var root = new JsonObject();
        foreach ((string nameSpace, var entries) in _spaces.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var space = new JsonObject();
            foreach ((string key, KvEntry entry) in entries)
            {
                space[key] = new JsonObject { ["type"] = TypeName(entry.Type), ["value"] = ToJson(entry) };
            }

            root[nameSpace] = space;
        }

        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(WriteOptions) + "\n");
        File.Move(temporary, _path, overwrite: true);
    }

    /// <summary>Formats a stored value for logs and tests (invariant culture).</summary>
    public static string Describe(KvEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ToJson(entry)?.ToJsonString() ?? string.Create(CultureInfo.InvariantCulture, $"{entry.Value.Length} bytes");
    }
}
