namespace Deck.Core.Icons;

// Guarda/resuelve el IconRef de un ButtonSlot (campo que ya existe en el
// modelo desde el arranque del proyecto, usado por Deck.Api/MobileDeck, pero
// que el Virtual Deck nunca leyó ni escribió hasta ahora). Convención de
// prefijo sobre el string libre: "file:<archivo>" para imágenes propias
// (copiadas a IconsDirectory), "emoji:<glyph>" para el set incluido en la
// app — no requiere tocar el schema de la base.
public sealed class IconStore
{
    public string IconsDirectory { get; }

    public IconStore(string appDataDir)
    {
        IconsDirectory = Path.Combine(appDataDir, "icons");
        Directory.CreateDirectory(IconsDirectory);
    }

    public async Task<string> SaveCustomIconAsync(Stream source, string extension, CancellationToken ct = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(IconsDirectory, fileName);

        await using var file = File.Create(path);
        await source.CopyToAsync(file, ct);

        return $"file:{fileName}";
    }

    public string? ResolveFilePath(string? iconRef)
    {
        if (iconRef is null || !iconRef.StartsWith("file:", StringComparison.Ordinal)) return null;

        var fileName = iconRef["file:".Length..];
        return Path.Combine(IconsDirectory, fileName);
    }

    public static string? ResolveEmoji(string? iconRef) =>
        iconRef is not null && iconRef.StartsWith("emoji:", StringComparison.Ordinal)
            ? iconRef["emoji:".Length..]
            : null;
}
