namespace Deck.Core.Credentials;

// Clave AES-256 para cifrar credenciales de plugin, guardada fuera de la base
// de datos (si alguien copia el .db no se lleva la clave). Se genera una vez
// por instalación y se reusa.
public static class CredentialEncryptionKey
{
    public static byte[] LoadOrCreate(string keyFilePath)
    {
        if (File.Exists(keyFilePath))
        {
            return Convert.FromBase64String(File.ReadAllText(keyFilePath).Trim());
        }

        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var directory = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(keyFilePath, Convert.ToBase64String(key));
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
