using System.IO.Ports;

namespace Deck.Device;

// Implementación real sobre System.IO.Ports — el ESP32-S3 aparece como puerto
// COM (Windows) o /dev/ttyUSB*, /dev/ttyACM* (Linux/macOS) al conectarlo por
// USB, mismo baud rate que setea el firmware (ver
// StreamDeckPlatform/Device/deck-pro-firmware-wokwi/src/main.cpp).
public sealed class SerialPortKeyEventSource : IKeyEventSource
{
    private readonly SerialPort _port;
    private string _buffer = string.Empty;

    public event Action<string>? LineReceived;

    public SerialPortKeyEventSource(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(portName, baudRate)
        {
            NewLine = "\n",
            DtrEnable = true, // el ESP32 no arranca a mandar por USB-CDC sin esto en algunas placas
        };
    }

    public void Open()
    {
        _port.DataReceived += (_, _) =>
        {
            // ReadExisting en vez de ReadLine: evita que un evento con una
            // línea a medias cuelgue esperando el "\n" que llega en el
            // próximo evento. Bufferea el resto sin "\n" hasta que complete.
            _buffer += _port.ReadExisting();
            var lines = _buffer.Split('\n');
            _buffer = lines[^1];
            for (var i = 0; i < lines.Length - 1; i++)
            {
                var line = lines[i].Trim();
                if (line.Length > 0) LineReceived?.Invoke(line);
            }
        };
        _port.Open();
    }

    public void Dispose()
    {
        if (_port.IsOpen) _port.Close();
        _port.Dispose();
    }
}
