# Flowdeck — StreamDeckPlatform

Plataforma de automatización multi-dispositivo. No es un clon de Stream Deck:
permite ejecutar acciones (streaming, multimedia, comunicación, domótica, APIs
propias) desde cualquier interfaz — escritorio, navegador, móvil o hardware
físico dedicado — todas hablando con el mismo Core.

Documentación completa de visión, fases y decisiones: panel de super admin en
`flowdeck.decatron.net` (interno).

## Estructura

```
StreamDeckPlatform.sln
├── Core/Deck.Core            → Modelo de datos + motor de ejecución. Sin UI.
├── Contracts/Deck.SDK        → Interfaces públicas para plugins (IPlugin, etc.)
├── UI/Deck.UI.Avalonia       → Virtual Deck (Windows/Linux/macOS, un solo código)
├── Api/Deck.Api              → ASP.NET Core + SignalR — sirve Web Deck y Mobile Deck
├── Device/Deck.Device        → Comunicación con hardware físico (HID/Serial/BLE)
├── Plugins/                  → Un proyecto por integración (OBS, Spotify, Discord, Twitch...)
│                                Se agregan uno a la vez, en su fase correspondiente.
└── Clients/                  → WebDeck y MobileDeck (Fase 7-8, todavía no arrancaron)
```

**Regla dura:** ningún plugin referencia a otro plugin. Todo se comparte vía
`Deck.Core` o `Deck.SDK`.

## Principios

1. Core sin UI — toda la lógica de negocio vive en una librería independiente.
2. Un solo protocolo de comunicación entre Core y todos los clientes.
3. Plugins aislados — un plugin caído no tumba el Core.
4. Validación incremental — un plugin a la vez, no se avanza hasta que el
   anterior está 100% estable.
5. Multiplataforma real desde el inicio (Avalonia: Windows/Linux/macOS).

## Estado actual

**Fase 1 — Core, completa.**

- `Deck.SDK`: contrato `IPlugin` (ciclo de vida, acciones, eventos), pensado
  para validarse con el primer plugin real en Fase 3 (OBS).
- `Deck.Core/Plugins`: `PluginManager` — carga dinámica de `.dll` vía
  `AssemblyLoadContext` coleccionable (se puede descargar en caliente),
  aislamiento de errores (ningún fallo de plugin llega a tumbar el proceso).
- `Deck.Core/Execution`: `ActionExecutor` — corre la lista ordenada de
  `ActionStep` de un botón o trigger; se corta en el primer paso que falla.
- `Deck.Core/Credentials`: Credential Manager real, SQLite + AES-256-GCM, cada
  plugin ve solo su propio namespace de credenciales.
- `Deck.Core/Data`: persistencia SQLite (EF Core) de perfiles, páginas,
  botones, pasos de acción y triggers.
- `Deck.Core.Tests`: 15 tests cubriendo lifecycle de plugin, ejecución de
  acciones falsas (abrir app / correr comando, con un plugin mock), carga
  dinámica real de un `.dll` separado, aislamiento de errores, cifrado de
  credenciales y round-trip de SQLite (incluye carpetas anidadas).

Siguiente: Fase 2 — UI Virtual Deck (Avalonia).

## Build y tests

```bash
dotnet build
dotnet test
```

Requiere .NET SDK 10.0.
