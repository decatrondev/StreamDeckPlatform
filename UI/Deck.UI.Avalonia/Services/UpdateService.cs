using Velopack;
using Velopack.Sources;

namespace Deck.UI.Avalonia.Services;

// Fase 9: la app se actualiza sola, sin instalador genérico y sin pedirle
// nada al usuario. GithubSource apunta directo a los Releases del repo
// público — no hace falta un feed propio.
//
// Decisión de UX (revisada): el chequeo pasó de "en background, aplicar
// recién cuando el proceso cierre solo" (WaitExitThenApplyUpdates) a
// "verificar y aplicar ANTES de abrir cualquier ventana" —
// ApplyUpdatesAndRestart corta el proceso actual y relanza la versión
// nueva, así que se llama desde Program.cs antes de construir la app de
// Avalonia. El esquema anterior dependía de que el proceso terminara de
// forma prolija para disparar el hook de salida; si el usuario cerraba
// Flowdeck de otra forma (matar el proceso, apagar la PC), la actualización
// quedaba descargada pero nunca se aplicaba — de ahí que pareciera que el
// auto-update "dejó de funcionar". Este approach no depende de eso: se
// resuelve entero antes de que exista ninguna ventana que cerrar.
public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/decatrondev/StreamDeckPlatform";

    private readonly UpdateManager _manager;

    public UpdateService()
    {
        _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    }

    // Bloquea el arranque a propósito — es la contracara de la decisión de
    // UX de arriba. Si hay una actualización, este método nunca retorna: el
    // proceso actual termina y Velopack relanza la versión nueva.
    public async Task CheckAndApplyBeforeLaunchAsync(string[] restartArgs)
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
            // Sin conexión, GitHub caído, rate limit — nunca bloquea el
            // arranque, se reintenta la próxima vez que se abra Flowdeck.
            return;
        }

        if (updateInfo is null) return;

        await _manager.DownloadUpdatesAsync(updateInfo);

        _manager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease, restartArgs);
    }
}
