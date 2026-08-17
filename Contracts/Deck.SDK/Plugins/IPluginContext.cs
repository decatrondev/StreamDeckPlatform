using Deck.SDK.Credentials;
using Microsoft.Extensions.Logging;

namespace Deck.SDK.Plugins;

// Lo que el Core le entrega a un plugin en Initialize — su única puerta de
// entrada a servicios del Core. Un plugin no debe acceder al Core de ninguna
// otra forma.
public interface IPluginContext
{
    ICredentialStore Credentials { get; }
    ILogger Logger { get; }
}
