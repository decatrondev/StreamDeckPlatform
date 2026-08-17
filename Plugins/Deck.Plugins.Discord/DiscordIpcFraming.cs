using System.Buffers.Binary;

namespace Deck.Plugins.Discord;

// Framing binario de Discord IPC: 4 bytes de opcode + 4 bytes de largo
// (little-endian), seguido del payload JSON. Igual en Windows (named pipe) y
// Unix (socket de dominio) — la diferencia entre plataformas es solo cómo se
// abre el Stream, no cómo se lee/escribe sobre él.
public static class DiscordIpcFraming
{
    public static async Task WriteFrameAsync(Stream stream, int opcode, byte[] payload, CancellationToken ct)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), payload.Length);

        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<(int Opcode, byte[] Payload)> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = await ReadExactlyAsync(stream, 8, ct);
        var opcode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));

        var payload = length > 0 ? await ReadExactlyAsync(stream, length, ct) : [];
        return (opcode, payload);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0) throw new IOException("La conexión IPC con Discord se cerró de forma inesperada.");
            offset += read;
        }

        return buffer;
    }
}
