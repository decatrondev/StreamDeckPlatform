using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Deck.Core.Execution;

// Variables tipo {dia}/{fecha}/{hora} que el usuario puede escribir en
// cualquier campo de texto de cualquier acción (mensaje de chat, título,
// argumentos de un comando, etc.) — se resuelven acá, en ActionExecutor,
// justo antes de ejecutar cada paso, nunca al guardar la tecla (si se
// resolvieran al guardar, {dia} quedaría fijo en el día en que se armó la
// tecla en vez de actualizarse cada vez que se aprieta).
//
// Solo cubre las variables "gratis" (reloj local, sin red) — las que
// necesitan preguntarle a Twitch en vivo (categoría, título, viewers,
// último seguidor) quedan para una pasada aparte, necesitan acceso a un
// plugin conectado, no son un reemplazo de texto puro como este.
public static class TemplateVariables
{
    public static string Apply(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson)) return parametersJson ?? "";

        JsonDocument doc;
        try { doc = JsonDocument.Parse(parametersJson); }
        catch { return parametersJson; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return parametersJson;

            var now = DateTimeOffset.Now;
            var es = CultureInfo.GetCultureInfo("es-ES");
            var values = new Dictionary<string, string>
            {
                ["{dia}"] = now.ToString("dddd", es),
                ["{fecha}"] = now.ToString("dd/MM/yyyy", es),
                ["{hora}"] = now.ToString("HH:mm", es),
            };

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        writer.WriteStringValue(ReplaceAll(property.Value.GetString() ?? "", values));
                    }
                    else
                    {
                        property.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static string ReplaceAll(string text, Dictionary<string, string> values)
    {
        foreach (var (token, value) in values)
        {
            text = text.Replace(token, value, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }
}
