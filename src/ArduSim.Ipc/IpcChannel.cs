using System.Buffers.Binary;
using MessagePack;

namespace ArduSim.Ipc;

/// <summary>Length-prefixed MessagePack frames over a stream (ADR-003): 4-byte little-endian length, then the payload.</summary>
public sealed class IpcChannel(Stream stream) : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public static byte[] Serialize(IpcMessage message) => MessagePackSerializer.Serialize(message);

    public static IpcMessage Deserialize(ReadOnlyMemory<byte> payload) => MessagePackSerializer.Deserialize<IpcMessage>(payload);

    public async Task WriteAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        byte[] payload = Serialize(message);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Reads the next message, or null when the other side closed the stream.</summary>
    public async Task<IpcMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        if (!await ReadExactlyAsync(header, cancellationToken))
        {
            return null;
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > IpcProtocol.MaxFrameBytes)
        {
            throw new InvalidDataException($"IPC frame of {length} bytes is outside 0..{IpcProtocol.MaxFrameBytes}.");
        }

        byte[] payload = new byte[length];
        if (!await ReadExactlyAsync(payload, cancellationToken))
        {
            return null;
        }

        return Deserialize(payload);
    }

    public async ValueTask DisposeAsync()
    {
        await stream.DisposeAsync();
        _writeLock.Dispose();
    }

    private async Task<bool> ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
