using System.Text.Json;
using Deck.Plugins.Discord.Tests.Fakes;
using Deck.SDK.Credentials;
using Deck.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deck.Plugins.Discord.Tests;

public class DiscordPluginTests : IAsyncLifetime
{
    private FakeDiscordIpcServer _ipcServer = null!;
    private FakeWebhookServer _webhookServer = null!;
    private InMemoryCredentialStore _credentials = null!;
    private DiscordPlugin _plugin = null!;

    public async Task InitializeAsync()
    {
        _ipcServer = new FakeDiscordIpcServer();
        await _ipcServer.StartAsync();

        _webhookServer = new FakeWebhookServer();
        await _webhookServer.StartAsync();

        _credentials = new InMemoryCredentialStore();

        _plugin = new DiscordPlugin("test-client-id", _ipcServer.PipeIndex, _ipcServer.UnixSocketDirOverride, new HttpClient());
        await _plugin.InitializeAsync(new TestPluginContext(_credentials));
    }

    public async Task DisposeAsync()
    {
        await _plugin.DisposeAsync();
        await _ipcServer.DisposeAsync();
        await _webhookServer.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_CompletesHandshake_ActionsWorkAfterward()
    {
        await ConnectAsync();

        var result = await _plugin.ExecuteActionAsync("toggle-mute", "{}");

        Assert.True(result.Success, result.Message);
        Assert.Contains(_ipcServer.ReceivedCommands, c => c.Cmd == "GET_VOICE_SETTINGS");
        Assert.Contains(_ipcServer.ReceivedCommands, c => c.Cmd == "SET_VOICE_SETTINGS");
    }

    [Fact]
    public async Task ExecuteActionAsync_ToggleMute_FlipsCurrentState()
    {
        _ipcServer.VoiceMuted = false;
        await ConnectAsync();

        var result = await _plugin.ExecuteActionAsync("toggle-mute", "{}");

        Assert.True(result.Success);
        Assert.True(_ipcServer.VoiceMuted, "Debería haber quedado muteado tras el toggle.");
    }

    [Fact]
    public async Task ExecuteActionAsync_SetVoiceChannel_SendsChannelId()
    {
        await ConnectAsync();

        var result = await _plugin.ExecuteActionAsync("set-voice-channel", """{"channelId":"12345"}""");

        Assert.True(result.Success);
        var call = Assert.Single(_ipcServer.ReceivedCommands, c => c.Cmd == "SELECT_VOICE_CHANNEL");
        Assert.Equal("12345", call.Args!.Value.GetProperty("channel_id").GetString());
    }

    [Fact]
    public async Task ExecuteActionAsync_WithoutConnecting_FailsGracefully_DoesNotThrow()
    {
        var result = await _plugin.ExecuteActionAsync("toggle-mute", "{}");

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_RejectedCommand_FailsGracefully_DoesNotThrow()
    {
        await ConnectAsync();
        _ipcServer.RejectNextCommand = true;

        var result = await _plugin.ExecuteActionAsync("toggle-mute", "{}");

        Assert.False(result.Success);
        Assert.Contains("4000", result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_SendMessage_WithoutWebhookConfigured_FailsGracefully()
    {
        await ConnectAsync();

        var result = await _plugin.ExecuteActionAsync("send-message", """{"content":"hola"}""");

        Assert.False(result.Success);
        Assert.Contains("webhook", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteActionAsync_SendMessage_WithWebhookConfigured_Succeeds()
    {
        await _credentials.SetAsync("webhook-url", _webhookServer.WebhookUrl.ToString());
        await ConnectAsync();

        var result = await _plugin.ExecuteActionAsync("send-message", """{"content":"hola flowdeck"}""");

        Assert.True(result.Success, result.Message);
        Assert.Contains("hola flowdeck", _webhookServer.ReceivedMessages);
    }

    [Fact]
    public async Task PluginEvent_VoiceStateUpdate_IsRelayedAsPluginEvent()
    {
        await ConnectAsync();

        PluginEvent? received = null;
        _plugin.EventRaised += (_, e) => { if (e.EventId == "voice-state-update") received ??= e; };

        await _ipcServer.BroadcastEventAsync("VOICE_STATE_UPDATE", new { mute = true });

        var ok = await PollUntilAsync(() => received is not null, timeoutMs: 3000);

        Assert.True(ok, "No se recibió el evento voice-state-update a tiempo.");
        using var doc = JsonDocument.Parse(received!.PayloadJson);
        Assert.True(doc.RootElement.GetProperty("mute").GetBoolean());
    }

    [Fact]
    public async Task Disconnection_ThenDiscordComesBack_ReconnectsAutomatically_ActionsWorkAgain()
    {
        await ConnectAsync();

        var before = await _plugin.ExecuteActionAsync("toggle-mute", "{}");
        Assert.True(before.Success);

        // Simula que se cerró Discord.
        await _ipcServer.DropConnectionAsync();

        await Task.Delay(500);
        var whileDown = await _plugin.ExecuteActionAsync("toggle-mute", "{}");
        Assert.False(whileDown.Success);

        // El cliente reintenta solo cada 3s.
        var reconnected = await PollUntilAsync(async () =>
        {
            var r = await _plugin.ExecuteActionAsync("toggle-mute", "{}");
            return r.Success;
        }, timeoutMs: 8000);

        Assert.True(reconnected, "El plugin debería haberse reconectado solo sin intervención externa.");
    }

    private async Task ConnectAsync()
    {
        await _plugin.ConnectAsync();
        var ok = await PollUntilAsync(() => _plugin.ConnectionState == DiscordConnectionState.Connected, timeoutMs: 5000);
        Assert.True(ok, $"El plugin no llegó a 'Connected' (quedó en '{_plugin.ConnectionState}').");
    }

    private static async Task<bool> PollUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var elapsed = 0;
        while (elapsed < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(100);
            elapsed += 100;
        }
        return condition();
    }

    private static async Task<bool> PollUntilAsync(Func<Task<bool>> condition, int timeoutMs)
    {
        var elapsed = 0;
        while (elapsed < timeoutMs)
        {
            if (await condition()) return true;
            await Task.Delay(200);
            elapsed += 200;
        }
        return await condition();
    }

    private sealed class TestPluginContext : IPluginContext
    {
        public TestPluginContext(ICredentialStore credentials) => Credentials = credentials;
        public ICredentialStore Credentials { get; }
        public Microsoft.Extensions.Logging.ILogger Logger { get; } = NullLogger.Instance;
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
