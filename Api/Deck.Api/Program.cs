using System.Text.Json.Serialization;
using Deck.Api.Auth;
using Deck.Api.Dtos;
using Deck.Api.Hubs;
using Deck.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(PairingKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, PairingKeyAuthenticationHandler>(PairingKeyAuthenticationHandler.SchemeName, null);
// FallbackPolicy en vez de [Authorize] por controller: seguro por default,
// un endpoint nuevo queda protegido aunque alguien se olvide del atributo —
// hay que optar explícitamente por público con .AllowAnonymous() (ver /api/ping).
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build());

// Enums como texto en JSON (REST y SignalR) — el default de System.Text.Json
// es número crudo, ilegible desde el cliente web sin repetir el mismo mapeo
// en TypeScript.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// El control de acceso real ya lo hace la pairing key (ver Auth/), no el
// origen — pensado para que cualquier dispositivo de la LAN del usuario
// (celular, otra compu) apunte acá con una IP que ni siquiera se conoce de
// antemano, no tiene sentido una lista fija de orígenes.
var allowedOrigins = builder.Configuration.GetSection("Deck:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebDeck", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var dbPath = builder.Configuration["Deck:DatabasePath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flowdeck-Api", "flowdeck.db");

using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
builder.Services.AddSingleton(await DeckApiHost.StartAsync(dbPath, bootstrapLoggerFactory));
builder.Services.AddSingleton<ClientSessionRegistry>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("WebDeck");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<DeckHub>("/hubs/deck").RequireAuthorization();

// Sin [Authorize]: a propósito. Un cliente nuevo necesita poder distinguir
// "dirección equivocada" (esto nunca responde) de "pairing key equivocada"
// (esto responde 200, pero /api/profiles te da 401) — si /ping también
// pidiera la key, ambos casos se verían idénticos desde la UI de conexión.
app.MapGet("/api/ping", () => Results.Ok(new { service = "Deck.Api" })).AllowAnonymous();

// El PluginManager vive dentro de DeckApiHost, no se resuelve por DI acá
// arriba (recién existe una vez terminado StartAsync) — se conecta el relay
// de eventos ya con la app armada, pero antes de aceptar conexiones.
var host = app.Services.GetRequiredService<DeckApiHost>();
var hubContext = app.Services.GetRequiredService<IHubContext<DeckHub>>();
host.Plugins.PluginEventReceived += (_, e) =>
{
    var message = new PluginEventMessage(e.PluginId, e.Event.EventId, e.Event.PayloadJson, e.Event.OccurredAt);
    _ = hubContext.Clients.All.SendAsync("PluginEvent", message);
};

app.Logger.LogInformation(
    "Pairing key (copiala en Web Deck / Mobile Deck para conectar): {PairingKey}", host.PairingKey);

app.Run();

public partial class Program;
