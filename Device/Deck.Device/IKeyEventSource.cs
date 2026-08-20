namespace Deck.Device;

// Abstrae de dónde vienen las líneas "KEY:<indice>:DOWN/UP" — un puerto serial
// real (SerialPortKeyEventSource) o, en tests, una fuente falsa. Mismo patrón
// que los Fake*Server de los plugins: nada de Deck.Device.Tests depende de
// hardware ni de un puerto COM real.
public interface IKeyEventSource : IDisposable
{
    event Action<string>? LineReceived;
    void Open();
}
