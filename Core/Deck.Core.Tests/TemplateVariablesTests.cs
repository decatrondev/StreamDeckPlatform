using System.Text.Json;
using Deck.Core.Execution;

namespace Deck.Core.Tests;

public class TemplateVariablesTests
{
    [Fact]
    public void Apply_ReplacesDiaFechaHora_InStringFields()
    {
        var json = """{"message":"hoy es {dia}, {fecha} a las {hora}"}""";

        var result = TemplateVariables.Apply(json);

        using var doc = JsonDocument.Parse(result);
        var message = doc.RootElement.GetProperty("message").GetString()!;

        Assert.DoesNotContain("{dia}", message);
        Assert.DoesNotContain("{fecha}", message);
        Assert.DoesNotContain("{hora}", message);
    }

    [Fact]
    public void Apply_LeavesNonStringFields_Untouched()
    {
        var json = """{"volume":50,"enabled":true}""";

        var result = TemplateVariables.Apply(json);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(50, doc.RootElement.GetProperty("volume").GetInt32());
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void Apply_TextWithoutVariables_IsUnchanged()
    {
        var json = """{"message":"hola sin variables"}""";

        var result = TemplateVariables.Apply(json);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("hola sin variables", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Apply_EmptyOrInvalidJson_ReturnsInputUnchanged_DoesNotThrow()
    {
        Assert.Equal("", TemplateVariables.Apply(""));
        Assert.Equal("no es json", TemplateVariables.Apply("no es json"));
    }

    [Fact]
    public void Apply_MultipleFields_ReplacesInEachOne()
    {
        var json = """{"title":"stream de {dia}","description":"empezó a las {hora}"}""";

        var result = TemplateVariables.Apply(json);

        using var doc = JsonDocument.Parse(result);
        Assert.DoesNotContain("{dia}", doc.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("{hora}", doc.RootElement.GetProperty("description").GetString());
    }
}
