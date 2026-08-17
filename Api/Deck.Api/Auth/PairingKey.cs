namespace Deck.Api.Auth;

// Secreto compartido que cualquier cliente (Web Deck, Mobile Deck, futuro
// hardware) tiene que mandar para poder controlar el Core — sin esto,
// cualquiera en la misma LAN podía ejecutar acciones (gap real de la Fase 7,
// su propio roadmap pedía "autenticación de acceso a la API"). Mismo patrón
// que CredentialEncryptionKey: se genera una vez por instalación, se guarda
// fuera de la base, con permisos restringidos.
//
// Simplificación consciente: un único secreto por instalación, sin cuentas
// de usuario ni expiración — el mismo criterio que un PIN de emparejamiento
// de un dispositivo Bluetooth. El usuario lo copia una vez desde el archivo
// (o el log de arranque) a cada cliente nuevo que quiera conectar.
public static class PairingKey
{
    public static string LoadOrCreate(string keyFilePath)
    {
        if (File.Exists(keyFilePath))
        {
            return File.ReadAllText(keyFilePath).Trim();
        }

        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var directory = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(keyFilePath, key);
        TryRestrictPermissions(keyFilePath);

        return key;
    }

    private static void TryRestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
