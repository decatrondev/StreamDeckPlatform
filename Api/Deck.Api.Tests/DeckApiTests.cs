using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deck.Api.Dtos;
using Deck.Core.Model;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Deck.Api.Tests;

public class DeckApiTests : IClassFixture<DeckApiFactory>, IAsyncLifetime
{
    // Mismo criterio que Program.cs: la API serializa enums como texto para
    // que el cliente web no tenga que mapear números a mano — el HttpClient
    // de este test tiene que leerlos con las mismas opciones.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    private readonly DeckApiFactory _factory;
    private HttpClient _client = null!;

    public DeckApiTests(DeckApiFactory factory) => _factory = factory;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetProfiles_ReturnsSeededDefaultProfile()
    {
        var profiles = await _client.GetFromJsonAsync<List<ProfileDto>>("/api/profiles");

        var profile = Assert.Single(profiles!);
        Assert.Equal("Principal", profile.Name);
    }

    [Fact]
    public async Task GetPage_ReturnsRootPageWithNoButtonsInitially()
    {
        var profile = (await _client.GetFromJsonAsync<List<ProfileDto>>("/api/profiles"))!.Single();

        var page = await _client.GetFromJsonAsync<PageDto>($"/api/pages/{profile.RootPageId}", JsonOptions);

        Assert.NotNull(page);
        Assert.Empty(page!.Buttons);
    }

    [Fact]
    public async Task CreatePage_ThenAssignActionButton_RoundTripsThroughRest()
    {
        var page = await CreatePageAsync("Página de prueba");

        var upsertResponse = await _client.PutAsJsonAsync(
            $"/api/pages/{page.Id}/buttons/0/0",
            new UpsertButtonSlotRequest(
                Row: 0, Column: 0, Type: ButtonSlotType.Action, TargetPageId: null,
                Label: "Saludo", IconRef: null,
                Steps: [new ActionStepDto(0, "system", "run-command", MakeEchoParams())]));

        upsertResponse.EnsureSuccessStatusCode();

        var reloaded = await _client.GetFromJsonAsync<PageDto>($"/api/pages/{page.Id}", JsonOptions);
        var button = Assert.Single(reloaded!.Buttons);
        Assert.Equal("Saludo", button.Label);
        Assert.Single(button.Steps);
    }

    [Fact]
    public async Task UpsertButtonSlot_FolderWithActionSteps_IsRejected()
    {
        var page = await CreatePageAsync("Página con carpeta");
        var targetPage = await CreatePageAsync("Destino");

        var response = await _client.PutAsJsonAsync(
            $"/api/pages/{page.Id}/buttons/0/0",
            new UpsertButtonSlotRequest(
                Row: 0, Column: 0, Type: ButtonSlotType.Folder, TargetPageId: targetPage.Id,
                Label: "Carpeta", IconRef: null,
                Steps: [new ActionStepDto(0, "system", "run-command", MakeEchoParams())]));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPlugins_ListsBuiltInSystemPluginAsConnected()
    {
        var plugins = await _client.GetFromJsonAsync<List<PluginDto>>("/api/plugins", JsonOptions);

        var system = Assert.Single(plugins!, p => p.Id == "system");
        Assert.Equal(Deck.Core.Plugins.PluginState.Connected, system.State);
        Assert.Contains(system.Actions, a => a.Id == "run-command");
    }

    [Fact]
    public async Task Hub_ExecuteButton_RunsRealActionAndReportsSuccess()
    {
        var page = await CreatePageAsync("Página hub — acción");
        await _client.PutAsJsonAsync(
            $"/api/pages/{page.Id}/buttons/1/2",
            new UpsertButtonSlotRequest(
                Row: 1, Column: 2, Type: ButtonSlotType.Action, TargetPageId: null,
                Label: null, IconRef: null,
                Steps: [new ActionStepDto(0, "system", "run-command", MakeEchoParams())]));

        await using var connection = BuildHubConnection();
        await connection.StartAsync();

        var result = await connection.InvokeAsync<ExecuteButtonResult>("ExecuteButton", page.Id, 1, 2);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.NavigatedToPageId);
        Assert.Single(result.StepResults!);
    }

    [Fact]
    public async Task Hub_ExecuteButton_OnFolderSlot_ReturnsTargetPageId_WithoutRunningActions()
    {
        var page = await CreatePageAsync("Página hub — carpeta");
        var target = await CreatePageAsync("Página hub — destino");

        await _client.PutAsJsonAsync(
            $"/api/pages/{page.Id}/buttons/0/0",
            new UpsertButtonSlotRequest(
                Row: 0, Column: 0, Type: ButtonSlotType.Folder, TargetPageId: target.Id,
                Label: "Ir a destino", IconRef: null, Steps: null));

        await using var connection = BuildHubConnection();
        await connection.StartAsync();

        var result = await connection.InvokeAsync<ExecuteButtonResult>("ExecuteButton", page.Id, 0, 0);

        Assert.True(result.Success);
        Assert.Equal(target.Id, result.NavigatedToPageId);
        Assert.Null(result.StepResults);
    }

    [Fact]
    public async Task Hub_ExecuteButton_OnEmptySlot_FailsGracefully_DoesNotThrow()
    {
        var page = await CreatePageAsync("Página hub — vacía");

        await using var connection = BuildHubConnection();
        await connection.StartAsync();

        var result = await connection.InvokeAsync<ExecuteButtonResult>("ExecuteButton", page.Id, 9, 9);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Hub_PluginEvent_IsBroadcastToAllConnectedClients()
    {
        await using var listenerA = BuildHubConnection();
        await using var listenerB = BuildHubConnection();

        var receivedByA = new TaskCompletionSource<PluginEventMessage>();
        var receivedByB = new TaskCompletionSource<PluginEventMessage>();
        listenerA.On<PluginEventMessage>("PluginEvent", msg => receivedByA.TrySetResult(msg));
        listenerB.On<PluginEventMessage>("PluginEvent", msg => receivedByB.TrySetResult(msg));

        await listenerA.StartAsync();
        await listenerB.StartAsync();

        var host = _factory.Services.GetRequiredService<Deck.Api.Services.DeckApiHost>();
        var testPlugin = host.Plugins.LoadInstance(new Fakes.TestEventPlugin());
        await host.Plugins.InitializeAsync(testPlugin.Metadata.Id);
        await host.Plugins.ConnectAsync(testPlugin.Metadata.Id); // dispara el evento "test-event"

        var messageA = await WaitAsync(receivedByA.Task);
        var messageB = await WaitAsync(receivedByB.Task);

        Assert.Equal("test-event", messageA.EventId);
        Assert.Equal("test-event", messageB.EventId);
    }

    private async Task<PageDto> CreatePageAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/pages", new CreatePageRequest(name, Rows: 3, Columns: 5));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;
    }

    private HubConnection BuildHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/deck"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

    private static string MakeEchoParams()
    {
        var (path, args) = OperatingSystem.IsWindows() ? ("cmd.exe", "/c echo hola") : ("/bin/sh", "-c \"echo hola\"");
        return System.Text.Json.JsonSerializer.Serialize(new { path, args });
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, int timeoutMs = 5000)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (completed != task) throw new TimeoutException("No llegó el mensaje a tiempo.");
        return await task;
    }
}
