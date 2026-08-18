using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Deck.Core.Execution;

// Variables tipo {dia}/{fecha}/{hora}/{categoria}/etc. que el usuario puede
// escribir en cualquier campo de texto de cualquier acción (mensaje de chat,
// título, argumentos de un comando, etc.) — se resuelven acá, en
// ActionExecutor, justo antes de ejecutar cada paso, nunca al guardar la
// tecla (si se resolvieran al guardar, {dia} quedaría fijo en el día en que
// se armó la tecla en vez de actualizarse cada vez que se aprieta).
public static class TemplateVariables
{
    // Las que necesitan preguntarle a Twitch en vivo (vía DecatronPlugin) —
    // ActionExecutor solo se toma la molestia de pedirlas si alguna aparece
    // en el JSON, para no pegarle a la API en cada tecla que no las usa.
    public static readonly string[] LiveTokens = ["{categoria}", "{titulo}", "{viewers}", "{ultimo_seguidor}"];

    public static bool ContainsLiveToken(string? parametersJson) =>
        !string.IsNullOrEmpty(parametersJson) &&
        LiveTokens.Any(token => parametersJson.Contains(token, StringComparison.OrdinalIgnoreCase));

    public static string Apply(string? parametersJson, IReadOnlyDictionary<string, string>? liveValues = null)
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

            if (liveValues is not null)
            {
                foreach (var (key, value) in liveValues) values[key] = value;
            }

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
