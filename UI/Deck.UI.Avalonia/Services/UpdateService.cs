using Velopack;
using Velopack.Sources;

namespace Deck.UI.Avalonia.Services;

// Fase 9: la app se actualiza sola, sin instalador genérico y sin pedirle
// nada al usuario. GithubSource apunta directo a los Releases del repo
// público — no hace falta un feed propio.
//
// Decisión de UX (revisada): el chequeo pasó de "en background, aplicar
// recién cuando el proceso cierre solo" a "verificar y aplicar ANTES de
// abrir cualquier ventana" — se llama desde App.axaml.cs con la ventana de
// arranque (SplashWindow) ya mostrada, antes de construir la ventana
// principal. El esquema viejo dependía de que el proceso terminara de forma
// prolija para disparar el hook de salida; si el usuario cerraba Flowdeck de
// otra forma (matar el proceso, apagar la PC), la actualización quedaba
// descargada pero nunca se aplicaba.
//
// OJO con el método usado para aplicar: ApplyUpdatesAndRestart (probado en
// v0.0.5/v0.0.6) corta el proceso y relanza directo, pero NO tiene parámetro
// "silent" — a diferencia de WaitExitThenApplyUpdates, siempre muestra su
// propio diálogo nativo de Windows ("Actualización de Flowdeck / Instalando
// actualización...") mientras aplica, que es justo lo que NO queríamos (la
// marca propia se rompía ahí). Por eso se usa WaitExitThenApplyUpdates con
// silent:true de nuevo, pero ahora ANTES de mostrar la ventana principal (no
// en background con la app abierta, que era el problema original) — y como
// ese método no corta el proceso por sí solo (solo le avisa al updater que
// espere a que salga), hay que llamar Environment.Exit inmediatamente
// después. No hay nada que limpiar en este punto porque todavía no se
// construyó ninguna ventana real.
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
    // onStatus reporta cada etapa para que el caller la muestre en la
    // ventana de arranque (ver App.axaml.cs / SplashWindow).
    public async Task CheckAndApplyBeforeLaunchAsync(string[] restartArgs, Action<string>? onStatus = null)
    {
        // Corriendo desde el IDE (dotnet run) o portable sin instalar vía
        // Velopack: no hay nada que chequear, y CheckForUpdatesAsync tira si
        // se lo intenta de todas formas.
        if (!_manager.IsInstalled) return;

        onStatus?.Invoke("Verificando actualizaciones…");

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

        onStatus?.Invoke("Descargando actualización…");
        await _manager.DownloadUpdatesAsync(updateInfo);

        onStatus?.Invoke("Instalando actualización…");
        _manager.WaitExitThenApplyUpdates(updateInfo.TargetFullRelease, silent: true, restart: true, restartArgs: restartArgs);
        Environment.Exit(0);
    }
}
