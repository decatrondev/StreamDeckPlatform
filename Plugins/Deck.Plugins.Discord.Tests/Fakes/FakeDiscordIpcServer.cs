using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using Deck.Plugins.Discord;

namespace Deck.Plugins.Discord.Tests.Fakes;

// Sirve el mismo framing y handshake que el Discord real (named pipe en
// Windows, socket de dominio en Linux/macOS) para poder probar
// DiscordIpcClient de punta a punta sin necesitar el cliente de Discord
// instalado en el runner de CI.
public sealed class FakeDiscordIpcServer : IAsyncDisposable
{
    private readonly ConcurrentQueue<(string Cmd, JsonElement? Args)> _receivedCommands = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private Stream? _currentStream;

    public int PipeIndex { get; } = Random.Shared.Next(1000, 999_999);
    public string? UnixSocketDirOverride { get; }

    public bool RespondReady { get; set; } = true;
    public bool VoiceMuted { get; set; }
    public bool RejectNextCommand { get; set; }

    public IReadOnlyCollection<(string Cmd, JsonElement? Args)> ReceivedCommands => _receivedCommands.ToArray();

    public FakeDiscordIpcServer()
    {
        if (!OperatingSystem.IsWindows())
        {
            UnixSocketDirOverride = Path.Combine(Path.GetTempPath(), $"deck-discord-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(UnixSocketDirOverride);
        }
    }

    public Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_currentStream is not null)
        {
            try { await _currentStream.DisposeAsync(); } catch { /* ya cerrada */ }
        }

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* cancelado o timeout */ }
        }
    }

    // Simula "se cerró Discord": corta la conexión activa. El accept loop
    // sigue vivo y vuelve a escuchar, así que el cliente se puede reconectar
    // después (como si el usuario hubiera vuelto a abrir Discord).
    public async Task DropConnectionAsync()
    {
        var stream = _currentStream;
        _currentStream = null;

        if (stream is not null)
        {
            try { await stream.DisposeAsync(); } catch { /* ya cerrada */ }
        }
    }

    public Task BroadcastEventAsync(string evt, object data, CancellationToken ct = default)
    {
        var stream = _currentStream;
        if (stream is null) return Task.CompletedTask;

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { cmd = "DISPATCH", evt, data });
        return DiscordIpcFraming.WriteFrameAsync(stream, DiscordIpcOpCode.Frame, payload, ct);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = await AcceptOneAsync(ct);
            }
            catch
            {
                return;
            }

            _currentStream = stream;

            try
            {
                await HandleConnectionAsync(stream, ct);
            }
            catch
            {
                // se cortó (a propósito o no) — el loop sigue para la próxima conexión.
            }
        }
    }

    private async Task<Stream> AcceptOneAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeServerStream(
                $"discord-ipc-{PipeIndex}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(ct);
            return pipe;
        }

        var path = DiscordIpcTransport.GetUnixSocketPath(PipeIndex, UnixSocketDirOverride);
        if (File.Exists(path)) File.Delete(path);

        using var listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listenSocket.Bind(new UnixDomainSocketEndPoint(path));
        listenSocket.Listen(1);

        var accepted = await listenSocket.AcceptAsync(ct);
        return new NetworkStream(accepted, ownsSocket: true);
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken ct)
    {
        var (opcode, payload) = await DiscordIpcFraming.ReadFrameAsync(stream, ct);
        if (opcode != DiscordIpcOpCode.Handshake) return;

        if (!RespondReady)
        {
            // Simula un Discord que no contesta bien — el cliente tiene que
            // ver esto como un fallo de handshake, no como un READY.
            var badPayload = JsonSerializer.SerializeToUtf8Bytes(new { cmd = "DISPATCH", evt = "NOT_READY" });
            await DiscordIpcFraming.WriteFrameAsync(stream, DiscordIpcOpCode.Frame, badPayload, ct);
            return;
        }

        var readyPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            cmd = "DISPATCH",
            evt = "READY",
            data = new { v = 1 }
        });
        await DiscordIpcFraming.WriteFrameAsync(stream, DiscordIpcOpCode.Frame, readyPayload, ct);

        while (!ct.IsCancellationRequested)
        {
            var (cmdOpcode, cmdPayload) = await DiscordIpcFraming.ReadFrameAsync(stream, ct);
            if (cmdOpcode == DiscordIpcOpCode.Close) return;
            if (cmdOpcode != DiscordIpcOpCode.Frame) continue;

            await HandleCommandAsync(stream, cmdPayload, ct);
        }
    }

    private async Task HandleCommandAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var cmd = root.GetProperty("cmd").GetString()!;
        var nonce = root.GetProperty("nonce").GetString()!;
        JsonElement? args = root.TryGetProperty("args", out var a) && a.ValueKind != JsonValueKind.Null ? a.Clone() : null;

        _receivedCommands.Enqueue((cmd, args));

        if (RejectNextCommand)
        {
            RejectNextCommand = false;
            await WriteResponseAsync(stream, cmd, nonce, ct, error: (4000, "Comando rechazado (simulado)."));
            return;
        }

        switch (cmd)
        {
            case "GET_VOICE_SETTINGS":
                await WriteResponseAsync(stream, cmd, nonce, ct, data: new { mute = VoiceMuted });
                break;

            case "SET_VOICE_SETTINGS":
                if (args is { } setArgs && setArgs.TryGetProperty("mute", out var muteEl))
                {
                    VoiceMuted = muteEl.GetBoolean();
                }
                await WriteResponseAsync(stream, cmd, nonce, ct, data: new { mute = VoiceMuted });
                break;

            case "SELECT_VOICE_CHANNEL":
                await WriteResponseAsync(stream, cmd, nonce, ct, data: new { });
                break;

            case "SUBSCRIBE":
                await WriteResponseAsync(stream, cmd, nonce, ct, data: new { });
                break;

            default:
                await WriteResponseAsync(stream, cmd, nonce, ct, error: (4004, $"Comando desconocido: '{cmd}'."));
                break;
        }
    }

    private static Task WriteResponseAsync(
        Stream stream, string cmd, string nonce, CancellationToken ct,
        object? data = null, (int Code, string Message)? error = null)
    {
        object message = error is { } e
            ? new { cmd, evt = "ERROR", nonce, data = new { code = e.Code, message = e.Message } }
            : new { cmd, nonce, data };

        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        return DiscordIpcFraming.WriteFrameAsync(stream, DiscordIpcOpCode.Frame, payload, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        if (UnixSocketDirOverride is not null && Directory.Exists(UnixSocketDirOverride))
        {
            try { Directory.Delete(UnixSocketDirOverride, recursive: true); } catch { /* best effort */ }
        }
    }
}
