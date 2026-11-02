using System.IO.MemoryMappedFiles;

namespace ArduSim.Core.Simulation;

/// <summary>
/// EEPROM contents. The file-backed variant maps the project's <c>eeprom.bin</c>, so writes survive
/// even when the Runner is killed (Stop, crash). Erased bytes read 0xFF.
/// </summary>
public sealed class EepromMemory : IDisposable
{
    private readonly byte[]? _memory;
    private readonly MemoryMappedFile? _file;
    private readonly MemoryMappedViewAccessor? _view;

    private EepromMemory(int size, byte[]? memory, MemoryMappedFile? file, MemoryMappedViewAccessor? view)
    {
        Size = size;
        _memory = memory;
        _file = file;
        _view = view;
    }

    public int Size { get; }

    public static EepromMemory InMemory(int size)
    {
        var memory = new byte[size];
        Array.Fill(memory, (byte)0xFF);
        return new EepromMemory(size, memory, null, null);
    }

    public static EepromMemory FileBacked(string path, int size)
    {
        long existing = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (existing < size)
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            stream.Seek(existing, SeekOrigin.Begin);
            stream.Write(Enumerable.Repeat((byte)0xFF, size - (int)existing).ToArray());
        }

        MemoryMappedFile file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite);
        MemoryMappedViewAccessor view = file.CreateViewAccessor(0, size);
        return new EepromMemory(size, null, file, view);
    }

    public byte Read(int address) => _memory is not null ? _memory[address] : _view!.ReadByte(address);

    public void Write(int address, byte value)
    {
        if (_memory is not null)
        {
            _memory[address] = value;
        }
        else
        {
            _view!.Write(address, value);
        }
    }

    public void Dispose()
    {
        _view?.Dispose();
        _file?.Dispose();
    }
}
