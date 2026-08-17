using System.IO.Pipes;

namespace Deck.UI.Avalonia.Services;

// Sin esto cada doble-click (o cada apertura del acceso directo) lanzaba un
// proceso nuevo — varias ventanas de Flowdeck compitiendo por los mismos
// dispositivos. Un Mutex con nombre global decide quién es "la" instancia; el
// resto avisa por un named pipe y se cierra solo.
public static class SingleInstanceGuard
{
    private const string MutexName = "Flowdeck-SingleInstance-9F3E9C2F";
    private const string PipeName = "Flowdeck-Activate-9F3E9C2F";

    private static Mutex? _mutex;

    // null si ya había una instancia corriendo (y ya se le avisó que se
    // traiga al frente) — en ese caso el caller debe cerrar sin abrir nada.
    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew) return true;

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("activate");
        }
        catch
        {
            // Si no se pudo avisar (la otra instancia está trabada o
            // arrancando todavía) no importa — de cualquier forma esta
            // instancia nueva no se abre.
        }

        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    // Corre en background durante toda la vida del proceso, escuchando
    // pedidos de activación de instancias posteriores que se cerraron solas.
    public static void ListenForActivationRequests(Action onActivateRequested)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server);
                    await reader.ReadLineAsync();
                    onActivateRequested();
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }
}
