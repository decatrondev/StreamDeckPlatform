using System.IO.Pipes;
using System.Net.Sockets;

namespace Deck.Plugins.Discord;

// Discord expone su IPC local como named pipe en Windows
// (\\.\pipe\discord-ipc-N) y como socket de dominio Unix en Linux/macOS
// ($XDG_RUNTIME_DIR/discord-ipc-N, con fallbacks). Una vez conectado, el
// resto del protocolo (framing, handshake, comandos) es idéntico — por eso
// esto devuelve un Stream genérico y no dos clientes separados.
public static class DiscordIpcTransport
{
    public static async Task<Stream> ConnectAsync(int pipeIndex, string? unixSocketDirectoryOverride, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{pipeIndex}", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(ct);
            return pipe;
        }

        var path = GetUnixSocketPath(pipeIndex, unixSocketDirectoryOverride);
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct);
        return new NetworkStream(socket, ownsSocket: true);
    }

    public static string GetUnixSocketPath(int pipeIndex, string? directoryOverride)
    {
        var directory = directoryOverride
            ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? Environment.GetEnvironmentVariable("TMPDIR")
            ?? Environment.GetEnvironmentVariable("TMP")
            ?? Environment.GetEnvironmentVariable("TEMP")
            ?? "/tmp";

        return Path.Combine(directory, $"discord-ipc-{pipeIndex}");
    }
}
