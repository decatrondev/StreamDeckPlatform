using System.Text.Json;
using Deck.Plugins.Obs.Tests.Fakes;
using Deck.SDK.Credentials;
using Deck.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deck.Plugins.Obs.Tests;

public class ObsPluginTests : IAsyncLifetime
{
    private FakeObsServer _server = null!;
    private ObsPlugin _plugin = null!;

    public async Task InitializeAsync()
    {
        _server = new FakeObsServer();
        await _server.StartAsync();

        _plugin = new ObsPlugin(_server.Uri);
        await _plugin.InitializeAsync(new TestPluginContext());
    }

    public async Task DisposeAsync()
    {
        await _plugin.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_CompletesHandshake_ActionsWorkAfterward()
    {
        await _plugin.ConnectAsync();
        await WaitForStateAsync(_plugin, ObsConnectionState.Connected);

        var result = await _plugin.ExecuteActionAsync("set-scene", """{"scene":"Gameplay"}""");

        Assert.True(result.Success);
        Assert.Contains("Gameplay", result.Message);
        Assert.Contains(_server.ReceivedRequests, r => r.RequestType == "SetCurrentProgramScene");
    }

    [Fact]
    public async Task ExecuteActionAsync_ToggleMute_SendsCorrectRequest()
    {
        await _plugin.ConnectAsync();
        await WaitForStateAsync(_plugin, ObsConnectionState.Connected);

        var result = await _plugin.ExecuteActionAsync("toggle-mute", """{"source":"Mic/Aux"}""");

        Assert.True(result.Success);
        var req = Assert.Single(_server.ReceivedRequests, r => r.RequestType == "ToggleInputMute");
        Assert.Equal("Mic/Aux", req.RequestData!.Value.GetProperty("inputName").GetString());
    }

    [Fact]
    public async Task ExecuteActionAsync_StartStopStreamAndRecord_AllSucceed()
    {
        await _plugin.ConnectAsync();
        await WaitForStateAsync(_plugin, ObsConnectionState.Connected);

        foreach (var action in new[] { "start-stream", "stop-stream", "start-record", "stop-record" })
        {
            var result = await _plugin.ExecuteActionAsync(action, "{}");
            Assert.True(result.Success, $"'{action}' debería haber tenido éxito: {result.Message}");
        }
    }

    [Fact]
    public async Task ExecuteActionAsync_WithoutConnecting_FailsGracefully_DoesNotThrow()
    {
        // Sin ConnectAsync — el plugin no debe tirar una excepción sin
        // capturar, tiene que volver un resultado fallido (aislamiento de
        // errores del contrato).
        var result = await _plugin.ExecuteActionAsync("set-scene", """{"scene":"X"}""");

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task GetParameterOptionsAsync_SetScene_ReturnsRealSceneNames()
    {
        _server.CannedResponses["GetSceneList"] = new
        {
            scenes = new[] { new { sceneName = "Gameplay" }, new { sceneName = "Charla" } }
        };

        await _plugin.ConnectAsync();
        await WaitForStateAsync(_plugin, ObsConnectionState.Connected);

        var options = await _plugin.GetParameterOptionsAsync("set-scene", "scene");

        Assert.Equal(["Gameplay", "Charla"], options.Select(o => o.Value));
    }

    [Fact]
    public async Task GetParameterOptionsAsync_ToggleMute_ReturnsRealInputNames()
    {
        _server.CannedResponses["GetInputList"] = new
        {
            inputs = new[] { new { inputName = "Mic/Aux" }, new { inputName = "Desktop Audio" } }
        };

        await _plugin.ConnectAsync();
        await WaitForStateAsync(_plugin, ObsConnectionState.Connected);

        var options = await _plugin.GetParameterOptionsAsync("toggle-mute", "source");

        Assert.Equal(["Mic/Aux", "Desktop Audio"], options.Select(o => o.Value));
    }

    [Fact]
    public async Task GetParameterOptionsAsync_UnknownField_ReturnsEmpty()
    {
        await _plugin.ConnectAsync();
        await WaitForStateAsync(_plugin, ObsConnectionState.Connected);

        var options = await _plugin.GetParameterOptionsAsync("start-stream", "whatever");

        Assert.Empty(options);
    }

    [Fact]
    public async Task GetParameterOptionsAsync_WithoutConnecting_ReturnsEmpty_DoesNotThrow()
    {
        var options = await _plugin.GetParameterOptionsAsync("set-scene", "scene");

        Assert.Empty(options);
    }

    [Fact]
    public async Task Connection_WithWrongPassword_EndsInAuthenticationFailure_WithoutCrashing()
    {
        _server.RequiredPassword = "correcta";

        await using var authPlugin = new ObsPlugin(_server.Uri);
        var context = new TestPluginContext();
        await context.Credentials.SetAsync("password", "incorrecta");
        await authPlugin.InitializeAsync(context);

        await authPlugin.ConnectAsync();
        await WaitForStateAsync(authPlugin, ObsConnectionState.AuthenticationFailed);

        var result = await authPlugin.ExecuteActionAsync("start-stream", "{}");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ConnectAsync_UsesStoredHostAndPort_WhenConfigured()
    {
        // El ctor apunta a un puerto que no existe — si ConnectAsync no
        // leyera host/port guardados en Ajustes, esto nunca conectaría.
        await using var plugin = new ObsPlugin(new Uri("ws://127.0.0.1:1"));
        var context = new TestPluginContext();
        await context.Credentials.SetAsync("host", _server.Uri.Host);
        await context.Credentials.SetAsync("port", _server.Uri.Port.ToString());
        await plugin.InitializeAsync(context);

        await plugin.ConnectAsync();
        await WaitForStateAsync(plugin, ObsConnectionState.Connected);
    }

    [Fact]
    public async Task Connection_WithCorrectPassword_Authenticates_ActionsWork()
    {
        _server.RequiredPassword = "correcta";

        await using var authPlugin = new ObsPlugin(_server.Uri);
        var context = new TestPluginContext();
        await context.Credentials.SetAsync("password", "correcta");
        await authPlugin.InitializeAsync(context);

        await authPlugin.ConnectAsync();
        await WaitForStateAsync(authPlugin, ObsConnectionState.Connected);

        var result = await authPlugin.ExecuteActionAsync("start-stream", "{}");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task PluginEvent_StreamStateChanged_IsRelayedAsPluginEvent()
    {
        await _plugin.ConnectAsync();
        await WaitForStateAsync(_plugin, ObsConnectionState.Connected);

        PluginEvent? received = null;
        _plugin.EventRaised += (_, e) => { if (e.EventId == "stream-state") received = e; };

        await _server.BroadcastEventAsync("StreamStateChanged", new { outputActive = true, outputState = "OBS_WEBSOCKET_OUTPUT_STARTED" });
        await PollUntilAsync(() => received is not null, 2000);

        Assert.NotNull(received);
        using var doc = JsonDocument.Parse(received!.PayloadJson);
        Assert.True(doc.RootElement.GetProperty("outputActive").GetBoolean());
    }

    [Fact]
    public async Task Disconnection_ThenServerComesBack_ReconnectsAutomatically_ActionsWorkAgain()
    {
        await _plugin.ConnectAsync();
        await WaitForStateAsync(_plugin, ObsConnectionState.Connected);

        var before = await _plugin.ExecuteActionAsync("start-stream", "{}");
        Assert.True(before.Success);

        // Simula que se cerró OBS.
        await _server.CloseAllConnectionsAsync();

        // Mientras está caído, no debe explotar — debe devolver un fallo prolijo.
        await Task.Delay(500);
        var whileDown = await _plugin.ExecuteActionAsync("start-stream", "{}");
        Assert.False(whileDown.Success);

        // El cliente reintenta solo cada 3s — esperamos a que vuelva.
        var reconnected = await PollUntilAsync(async () =>
        {
            var r = await _plugin.ExecuteActionAsync("start-stream", "{}");
            return r.Success;
        }, timeoutMs: 8000);

        Assert.True(reconnected, "El plugin debería haberse reconectado solo sin intervención externa.");
    }

    private static async Task WaitForStateAsync(ObsPlugin plugin, ObsConnectionState state, int timeoutMs = 5000)
    {
        var ok = await PollUntilAsync(() => plugin.ConnectionState == state, timeoutMs);
        Assert.True(ok, $"El plugin no llegó a '{state}' en {timeoutMs}ms (quedó en '{plugin.ConnectionState}').");
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
        public ICredentialStore Credentials { get; } = new InMemoryCredentialStore();
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
