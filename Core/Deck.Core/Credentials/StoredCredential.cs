namespace Deck.Core.Credentials;

// Fila de credencial cifrada en SQLite. El valor nunca se guarda en texto
// plano — CipherText + Nonce son la salida de AES-GCM (ver
// SqliteCredentialStore). La clave de cifrado vive fuera de la base, en un
// archivo local con permisos restrictivos.
public class StoredCredential
{
    public Guid Id { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public byte[] CipherText { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
}
