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
    public void Apply_LiveValues_ReplacesCategoriaTituloViewersUltimoSeguidor()
    {
        var json = """{"message":"jugando {categoria}, {viewers} viewers, gracias {ultimo_seguidor}"}""";
        var live = new Dictionary<string, string>
        {
            ["{categoria}"] = "Valorant",
            ["{viewers}"] = "123",
            ["{ultimo_seguidor}"] = "pepito",
        };

        var result = TemplateVariables.Apply(json, live);

        using var doc = JsonDocument.Parse(result);
        var message = doc.RootElement.GetProperty("message").GetString()!;

        Assert.Contains("Valorant", message);
        Assert.Contains("123", message);
        Assert.Contains("pepito", message);
    }

    [Fact]
    public void ContainsLiveToken_DetectsCategoriaTituloViewersUltimoSeguidor()
    {
        Assert.True(TemplateVariables.ContainsLiveToken("""{"message":"{categoria}"}"""));
        Assert.True(TemplateVariables.ContainsLiveToken("""{"title":"{titulo}"}"""));
        Assert.True(TemplateVariables.ContainsLiveToken("""{"message":"{viewers}"}"""));
        Assert.True(TemplateVariables.ContainsLiveToken("""{"message":"{ultimo_seguidor}"}"""));
    }

    [Fact]
    public void ContainsLiveToken_WithOnlyLocalVariables_ReturnsFalse()
    {
        Assert.False(TemplateVariables.ContainsLiveToken("""{"message":"{dia} {fecha} {hora}"}"""));
        Assert.False(TemplateVariables.ContainsLiveToken(null));
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
