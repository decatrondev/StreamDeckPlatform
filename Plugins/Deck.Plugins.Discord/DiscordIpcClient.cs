using System.Collections.Concurrent;
using System.Text.Json;

namespace Deck.Plugins.Discord;

// Cliente del protocolo IPC local de Discord — handshake, comandos
// correlacionados por nonce, y eventos suscritos (VOICE_STATE_UPDATE, etc.).
// Mismo patrón de reconexión automática que ObsWebSocketClient (Fase 3): el
// Core nunca reintenta por el plugin, así que el reintento vive acá adentro.
public sealed class DiscordIpcClient : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly string _clientId;
    private readonly int _pipeIndex;
    private readonly string? _unixSocketDirOverride;

    private Stream? _stream;
    private CancellationTokenSource? _lifecycleCts;
    private Task? _loopTask;
    private bool _hasEverConnected;

    public DiscordConnectionState State { get; private set; } = DiscordConnectionState.Disconnected;

    public event Action<DiscordConnectionState>? StateChanged;
    public event Action<string, JsonElement>? EventReceived;

    public DiscordIpcClient(string clientId, int pipeIndex = 0, string? unixSocketDirOverride = null)
    {
        _clientId = clientId;
        _pipeIndex = pipeIndex;
        _unixSocketDirOverride = unixSocketDirOverride;
    }

    public void Start()
    {
        _hasEverConnected = false;
        _lifecycleCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_lifecycleCts.Token));
    }

    public async Task StopAsync()
    {
        _lifecycleCts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask; } catch { /* el loop maneja sus propias excepciones */ }
        }

        await CloseStreamAsync();
        SetState(DiscordConnectionState.Disconnected);
    }

    public async Task<JsonElement> SendCommandAsync(string cmd, object? args, CancellationToken ct)
    {
        if (_stream is null || State != DiscordConnectionState.Connected)
        {
            throw new InvalidOperationException("No hay conexión activa con Discord.");
        }

        var nonce = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = tcs;

        await using var registration = ct.Register(() => tcs.TrySetCanceled(ct));

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { cmd, args, nonce });
        await DiscordIpcFraming.WriteFrameAsync(_stream, DiscordIpcOpCode.Frame, payload, ct);

        return await tcs.Task;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(_hasEverConnected ? DiscordConnectionState.Reconnecting : DiscordConnectionState.Connecting);
                await ConnectAndHandshakeAsync(ct);

                _hasEverConnected = true;
                SetState(DiscordConnectionState.Connected);

                await ReceiveLoopAsync(ct);

                if (!ct.IsCancellationRequested) SetState(DiscordConnectionState.Reconnecting);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                FailPendingRequests();
                return;
            }
            catch
            {
                // Discord cerrado, pipe no existe todavía, red local caída —
                // se resuelve reintentando abajo.
                if (!ct.IsCancellationRequested) SetState(DiscordConnectionState.Reconnecting);
            }

            FailPendingRequests();

            if (ct.IsCancellationRequested) return;

            try { await Task.Delay(ReconnectDelay, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConnectAndHandshakeAsync(CancellationToken ct)
    {
        await CloseStreamAsync();

        _stream = await DiscordIpcTransport.ConnectAsync(_pipeIndex, _unixSocketDirOverride, ct);

        var handshakePayload = JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = _clientId });
        await DiscordIpcFraming.WriteFrameAsync(_stream, DiscordIpcOpCode.Handshake, handshakePayload, ct);

        var (opcode, payload) = await DiscordIpcFraming.ReadFrameAsync(_stream, ct);
        using var doc = JsonDocument.Parse(payload);

        var evt = doc.RootElement.TryGetProperty("evt", out var evtEl) ? evtEl.GetString() : null;
        if (opcode != DiscordIpcOpCode.Frame || evt != "READY")
        {
            throw new IOException("Discord no respondió READY durante el handshake.");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            (int Opcode, byte[] Payload) frame;
            try
            {
                frame = await DiscordIpcFraming.ReadFrameAsync(_stream!, ct);
            }
            catch
            {
                return; // conexión cortada
            }

            if (frame.Opcode == DiscordIpcOpCode.Close) return;
            if (frame.Opcode != DiscordIpcOpCode.Frame) continue;

            HandleFrame(frame.Payload);
        }
    }

    private void HandleFrame(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var nonce = root.TryGetProperty("nonce", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        var evt = root.TryGetProperty("evt", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        if (nonce is not null && _pending.TryRemove(nonce, out var tcs))
        {
            if (evt == "ERROR")
            {
                var data = root.GetProperty("data");
                var code = data.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
                var message = data.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                tcs.TrySetException(new DiscordRpcException(code, message));
            }
            else
            {
                tcs.TrySetResult(root.TryGetProperty("data", out var d) ? d.Clone() : default);
            }

            return;
        }

        // Sin nonce pendiente que resolver: es un evento suscripto llegando
        // por su cuenta (DISPATCH), no la respuesta a un comando nuestro.
        if (evt is not null)
        {
            EventReceived?.Invoke(evt, root.TryGetProperty("data", out var ed) ? ed.Clone() : default);
        }
    }

    private void FailPendingRequests()
    {
        foreach (var key in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(key, out var tcs))
            {
                tcs.TrySetException(new IOException("Se perdió la conexión con Discord antes de recibir respuesta."));
            }
        }
    }

    private void SetState(DiscordConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private async Task CloseStreamAsync()
    {
        if (_stream is null) return;

        try { await _stream.DisposeAsync(); }
        catch { /* ya se estaba cerrando */ }
        finally { _stream = null; }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
