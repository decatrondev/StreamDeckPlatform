using Deck.Plugins.Discord;

namespace Deck.UI.Avalonia.Services;

// Mantiene el Rich Presence de Discord prendido todo el tiempo que Flowdeck
// esté conectado a Discord, esté o no en vivo — es publicidad constante
// (logos + botones a decatron.net/flowdeck.decatron.net), no algo atado a
// si estás transmitiendo. En vivo muestra un carrusel con datos reales
// (título/categoría/viewers/último seguidor); fuera de vivo muestra un
// estado genérico con el tiempo que lleva Flowdeck abierto.
public sealed class DiscordRichPresenceService : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    private static readonly DiscordActivityButton[] Buttons =
    [
        new("decatron.net", "https://decatron.net"),
        new("flowdeck.decatron.net", "https://flowdeck.decatron.net"),
    ];

    // Cada entrada arma el texto de "state" a partir de las variables en
    // vivo — null si ese dato no vino esta vez (ej. sin seguidor reciente),
    // en cuyo caso se salta ese ciclo sin romper nada.
    private static readonly Func<IReadOnlyDictionary<string, string>, string?>[] LiveStateRotation =
    [
        vars => vars.GetValueOrDefault("{categoria}"),
        vars => vars.TryGetValue("{viewers}", out var v) ? $"👀 {v} espectadores" : null,
        vars => vars.TryGetValue("{ultimo_seguidor}", out var f) ? $"🎉 Último seguidor: {f}" : null,
    ];

    private readonly DiscordPlugin _discord;
    private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> _getLiveVariables;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;
    private readonly long _appOpenedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private int _rotationIndex;

    public DiscordRichPresenceService(
        DiscordPlugin discord, Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> getLiveVariables)
    {
        _discord = discord;
        _getLiveVariables = getLiveVariables;
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch
            {
                // Discord cerrado, bot caído, sin conexión — se reintenta
                // solo en el próximo ciclo, no hace falta manejarlo acá.
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var vars = await _getLiveVariables(ct);
        var isLive = vars.ContainsKey("{titulo}");

        if (isLive)
        {
            var state = LiveStateRotation[_rotationIndex % LiveStateRotation.Length](vars);
            _rotationIndex++;

            if (state is null) return; // ese dato no vino esta vez — se prueba de nuevo en el próximo ciclo

            await _discord.SetActivityAsync(new DiscordActivity(
                Details: vars.GetValueOrDefault("{titulo}") ?? "En vivo",
                State: state,
                LargeImageKey: "decatron",
                LargeImageText: "Decatron",
                SmallImageKey: "flowdeck",
                SmallImageText: "Flowdeck",
                Buttons: Buttons), ct);
            return;
        }

        // Fuera de vivo: igual queda algo prendido en el perfil (publicidad
        // constante) en vez de apagarse — el cronómetro pasa a contar desde
        // que se abrió Flowdeck, no desde que se está en vivo.
        await _discord.SetActivityAsync(new DiscordActivity(
            Details: "🎛️ Preparando el stream",
            State: "Con Flowdeck",
            LargeImageKey: "decatron",
            LargeImageText: "Decatron",
            SmallImageKey: "flowdeck",
            SmallImageText: "Flowdeck",
            StartTimestamp: _appOpenedAtUnix,
            Buttons: Buttons), ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _loopTask; } catch { /* el loop maneja sus propias excepciones */ }
        _cts.Dispose();
    }
}
