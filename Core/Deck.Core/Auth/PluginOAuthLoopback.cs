using System.Diagnostics;
using System.Text;

namespace Deck.Core.Auth;

// El mismo baile de "abrir navegador, escuchar en loopback, devolver el
// code" que ya tenía DecatronAuthService, pero genérico — lo reusan los
// plugins con su propio OAuth directo contra el servicio real (Twitch,
// Spotify), que no pasan por el backend de Decatron. Cada llamador arma su
// propia URL de autorización (BeginAuthorization del plugin) y esta clase
// solo se encarga de la parte de UI/red que no depende del plugin.
public static class PluginOAuthLoopback
{
    public static async Task<string> WaitForCodeAsync(string authorizeUrl, string redirectUri, CancellationToken ct = default)
    {
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try
        {
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

            var contextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(3), ct);
            var completed = await Task.WhenAny(contextTask, timeoutTask);

            if (completed != contextTask)
                throw new TimeoutException("No llegó respuesta a tiempo — se canceló la conexión.");

            var context = await contextTask;
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            await RespondAsync(context, success: error is null && code is not null);

            if (error is not null) throw new InvalidOperationException($"Autorización rechazada: {error}");
            if (code is null) throw new InvalidOperationException("Respuesta inválida (falta el code).");

            return code;
        }
        finally
        {
            listener.Stop();
        }
    }

    // Misma pantalla de cierre que usa DecatronAuthService — se repite acá
    // en vez de compartirse porque son dos ensamblados sin dependencia entre
    // sí (Deck.Core no referencia nada de Deck.Plugins.*), y es HTML fijo.
    private static async Task RespondAsync(System.Net.HttpListenerContext context, bool success)
    {
        var (icon, heading, subtext) = success
            ? ("""<path d="M7 13l3 3 7-7" stroke="#2563EB" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>""",
               "Listo.",
               "Ya podés volver a Flowdeck — esta pestaña se cierra sola.")
            : ("""<path d="M8 8l8 8M16 8l-8 8" stroke="#EF4444" stroke-width="2.2" stroke-linecap="round"/>""",
               "Algo falló.",
               "Volvé a Flowdeck e intentá conectar de nuevo.");

        var html = $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8" />
            <title>Flowdeck</title>
            <style>
              :root { color-scheme: dark; }
              * { box-sizing: border-box; }
              body {
                margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
                background: #14171C; color: #E7EAEE;
                font-family: -apple-system, "Segoe UI", Inter, Roboto, sans-serif;
              }
              .card {
                width: 320px; padding: 28px 32px; text-align: center;
                background: #1C2028; border: 1px solid #2C323D; border-radius: 12px;
              }
              .brand { display: flex; align-items: center; justify-content: center; gap: 8px; margin-bottom: 22px; }
              .dot { width: 8px; height: 8px; border-radius: 50%; background: #2563EB; }
              .wordmark { font-size: 13px; font-weight: 600; letter-spacing: 0.01em; color: #E7EAEE; }
              .icon-ring {
                width: 44px; height: 44px; margin: 0 auto 16px; border-radius: 50%;
                display: flex; align-items: center; justify-content: center;
                background: rgba(37, 99, 235, 0.12);
              }
              h1 { font-size: 17px; font-weight: 600; margin: 0 0 6px; }
              p { font-size: 13px; line-height: 1.5; color: #8A94A6; margin: 0; }
            </style>
            </head>
            <body>
              <div class="card">
                <div class="brand"><span class="dot"></span><span class="wordmark">Flowdeck</span></div>
                <div class="icon-ring"><svg width="24" height="24" viewBox="0 0 24 24">{{icon}}</svg></div>
                <h1>{{heading}}</h1>
                <p>{{subtext}}</p>
              </div>
              <script>setTimeout(function () { window.close(); }, 900);</script>
            </body>
            </html>
            """;

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.OutputStream.Close();
    }
}
