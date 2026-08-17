using System.Security.Cryptography;
using System.Text;
using Deck.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Deck.Core.Credentials;

// Cifra con AES-256-GCM antes de persistir. Un nonce nuevo por valor guardado
// (nunca se reusa un nonce con la misma clave: eso es lo que rompe GCM).
public class SqliteCredentialManager : ICredentialManager
{
    private const int NonceSize = 12;
    private readonly DeckDbContext _db;
    private readonly byte[] _key;

    public SqliteCredentialManager(DeckDbContext db, byte[] encryptionKey)
    {
        _db = db;
        _key = encryptionKey;
    }

    public async Task<string?> GetAsync(string pluginId, string key, CancellationToken ct = default)
    {
        var row = await _db.Credentials
            .FirstOrDefaultAsync(c => c.PluginId == pluginId && c.Key == key, ct);

        if (row is null) return null;

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        var plaintext = new byte[row.CipherText.Length];
        aes.Decrypt(row.Nonce, row.CipherText, row.Tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    public async Task SetAsync(string pluginId, string key, string value, CancellationToken ct = default)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using (var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var existing = await _db.Credentials
            .FirstOrDefaultAsync(c => c.PluginId == pluginId && c.Key == key, ct);

        if (existing is null)
        {
            _db.Credentials.Add(new StoredCredential
            {
                Id = Guid.NewGuid(),
                PluginId = pluginId,
                Key = key,
                CipherText = ciphertext,
                Nonce = nonce,
                Tag = tag
            });
        }
        else
        {
            existing.CipherText = ciphertext;
            existing.Nonce = nonce;
            existing.Tag = tag;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string pluginId, string key, CancellationToken ct = default)
    {
        var existing = await _db.Credentials
            .FirstOrDefaultAsync(c => c.PluginId == pluginId && c.Key == key, ct);

        if (existing is null) return;

        _db.Credentials.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }
}
