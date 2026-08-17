using System.Text.Json.Serialization;
using Deck.Api.Dtos;
using Deck.Api.Hubs;
using Deck.Api.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Enums como texto en JSON (REST y SignalR) — el default de System.Text.Json
// es número crudo, ilegible desde el cliente web sin repetir el mismo mapeo
// en TypeScript.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Sin auth todavía (fuera de alcance de esta fase) y pensado para que
// cualquier dispositivo de la LAN del usuario (celular, otra compu) apunte
// acá con una IP que ni siquiera se conoce de antemano — no tiene sentido una
// lista fija de orígenes. Si en el futuro se agrega login, esto se restringe.
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
app.UseAuthorization();

app.MapControllers();
app.MapHub<DeckHub>("/hubs/deck");

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

app.Run();

public partial class Program;
