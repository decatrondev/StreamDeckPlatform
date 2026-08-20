using Deck.Plugins.Discord;

namespace Deck.UI.Avalonia.Services;

// Mientras el streamer está en vivo (según Decatron), mantiene el Rich
// Presence de Discord actualizado con un dato distinto cada ciclo — título
// fijo, y la línea de estado rotando entre categoría/viewers/último
// seguidor. Se apaga solo (ClearActivityAsync) apenas Decatron reporta que
// ya no está en vivo, para no dejar datos viejos pegados en el perfil.
public sealed class DiscordRichPresenceService : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    // Cada entrada es una función que arma el texto de "state" a partir de
    // las variables en vivo — null si ese dato no vino esta vez (ej. sin
    // seguidor reciente), en cuyo caso se salta ese ciclo sin romper nada.
    private static readonly Func<IReadOnlyDictionary<string, string>, string?>[] StateRotation =
    [
        vars => vars.GetValueOrDefault("{categoria}"),
        vars => vars.TryGetValue("{viewers}", out var v) ? $"👀 {v} espectadores" : null,
        vars => vars.TryGetValue("{ultimo_seguidor}", out var f) ? $"🎉 Último seguidor: {f}" : null,
    ];

    private readonly DiscordPlugin _discord;
    private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> _getLiveVariables;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;

    private int _rotationIndex;
    private bool _wasLive;

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

        if (!isLive)
        {
            if (_wasLive) await _discord.ClearActivityAsync(ct);
            _wasLive = false;
            return;
        }

        _wasLive = true;

        var state = StateRotation[_rotationIndex % StateRotation.Length](vars);
        _rotationIndex++;

        if (state is null) return; // ese dato no vino esta vez — se prueba de nuevo en el próximo ciclo

        await _discord.SetActivityAsync(new DiscordActivity(
            Details: vars.GetValueOrDefault("{titulo}") ?? "En vivo",
            State: state,
            LargeImageKey: "decatron",
            LargeImageText: "Decatron",
            SmallImageKey: "flowdeck",
            SmallImageText: "Flowdeck",
            Buttons:
            [
                new DiscordActivityButton("decatron.net", "https://decatron.net"),
                new DiscordActivityButton("flowdeck.decatron.net", "https://flowdeck.decatron.net"),
            ]), ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _loopTask; } catch { /* el loop maneja sus propias excepciones */ }
        _cts.Dispose();
    }
}
