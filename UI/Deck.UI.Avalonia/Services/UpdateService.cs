using Velopack;
using Velopack.Sources;

namespace Deck.UI.Avalonia.Services;

// Fase 9: la app se actualiza sola, sin instalador genérico y sin pedirle
// nada al usuario. GithubSource apunta directo a los Releases del repo
// público — no hace falta un feed propio.
//
// Decisión de UX: nunca se fuerza un restart en medio de una sesión (este es
// justo el tipo de app que alguien tiene abierta mientras transmite en vivo,
// reiniciarla sola sería el peor momento posible). En cambio se descarga en
// silencio y se aplica recién cuando el proceso cierra por su cuenta
// (WaitExitThenApplyUpdates) — la próxima vez que el usuario abre Flowdeck ya
// está en la versión nueva, sin haber visto ni un diálogo.
public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/decatrondev/StreamDeckPlatform";

    private readonly UpdateManager _manager;

    public event Action<string>? UpdateReady;

    public UpdateService()
    {
        _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    }

    public async Task CheckAndPrepareAsync(CancellationToken ct = default)
    {
        // Corriendo desde el IDE (dotnet run) o portable sin instalar vía
        // Velopack: no hay nada que chequear, y CheckForUpdatesAsync tira si
        // se lo intenta de todas formas.
        if (!_manager.IsInstalled) return;

        UpdateInfo? updateInfo;
        try
        {
            updateInfo = await _manager.CheckForUpdatesAsync();
        }
        catch
        {
            // Sin conexión, GitHub caído, rate limit — nunca bloquea el uso
            // normal de la app, se reintenta en el próximo arranque.
            return;
        }

        if (updateInfo is null) return;

        await _manager.DownloadUpdatesAsync(updateInfo, cancelToken: ct);

        _manager.WaitExitThenApplyUpdates(updateInfo.TargetFullRelease, silent: true, restart: true, restartArgs: []);
        UpdateReady?.Invoke(updateInfo.TargetFullRelease.Version.ToString());
    }
}
