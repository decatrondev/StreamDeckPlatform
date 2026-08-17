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
├── Plugins/
│   ├── Deck.Plugins.Obs       → Fase 3, completo.
│   └── Deck.Plugins.Spotify   → Fase 4, completo. Discord y Twitch se agregan
│                                uno a la vez, en su fase correspondiente.
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

**Fase 2 — UI Virtual Deck (Avalonia), completa.**

- Identidad visual propia: mismos tokens que `flowdeck.decatron.net` (grafito +
  azul + ámbar), ventana sin chrome nativo (titlebar, drag y botones min/max/
  cerrar 100% custom) — nada de wizard genérico de Windows.
- `Deck.Core/SystemActions`: acciones nativas del sistema (abrir app, ejecutar
  comando, abrir URL) registradas como un plugin más — mismo pipeline que
  cualquier plugin de Fase 3+, sin depender de ninguno todavía.
- Perfiles y páginas navegables (breadcrumb + volver), carpetas anidadas reales
  (un botón "carpeta" navega a otra página).
- Asignación de teclas por selector (diálogo propio, sin `MessageBox` nativo):
  elegís acción o carpeta, completás los parámetros, se persiste en SQLite.
- Probado de punta a punta con captura de pantalla (Xvfb): asignar una tecla,
  guardarla, cerrarla, volver a abrirla y ejecutar la acción real.

**Fase 3 — Plugin #1: OBS, completa.**

- `Deck.Plugins.Obs`: primer plugin real, sobre `Deck.SDK` únicamente (sin
  referenciar `Deck.Core`, como marca la regla dura). Cliente propio del
  protocolo obs-websocket v5 (JSON sobre WebSocket, sin dependencias de
  terceros) — sin OAuth, valida el patrón de Credential Manager con la
  contraseña opcional de obs-websocket.
- Acciones: cambiar escena, mutear/desmutear fuente, iniciar/detener stream,
  iniciar/detener grabación.
- Eventos: `stream-state` y `record-state` se relayan como `PluginEvent`, más
  `connection-state` para que la UI pueda reflejar conectado/reconectando/caído.
- Reconexión automática propia (el Core nunca reintenta por el plugin, es
  responsabilidad del plugin): reintenta cada 3s ante una caída de red o cierre
  de OBS; una contraseña inválida NO reintenta sola (queda en
  `AuthenticationFailed`, evita spam de intentos con credenciales que no van a
  funcionar).
- `Deck.Plugins.Obs.Tests`: servidor OBS falso propio (protocolo v5 real sobre
  `HttpListener`, sin depender de una instancia real de OBS en CI) — 8 tests:
  handshake, las 6 acciones, autenticación correcta/incorrecta sin crashear,
  relay de eventos, y reconexión automática real tras un cierre de conexión.
  Checklist de "plugin listo" (sección 7) cumplido.

**Fase 4 — Plugin #2: Spotify (OAuth real), completa.**

- `Deck.Plugins.Spotify`: primera integración con OAuth de verdad —
  Authorization Code + PKCE, sin client secret embebido (no hace falta:
  PKCE es justo el flujo pensado para apps distribuidas). Valida que el
  Credential Manager de Fase 1 (SQLite + AES-GCM) sirve tal cual para guardar
  un `refresh_token`, no solo la contraseña simple que usaba OBS.
- `BeginAuthorization`/`CompleteAuthorizationAsync` quedan fuera de `IPlugin`
  a propósito — el contrato genérico no modela "abrir el navegador y
  loguearse", eso lo maneja quien construya la UI de conexión (Fase 8+).
- Acciones: reproducir, pausar, siguiente, anterior, volumen.
- Evento `track-changed`: Spotify no tiene webhook de "canción cambió", así
  que se hace polling liviano (cada 5s) mientras está conectado, emitiendo
  el evento solo cuando el track realmente cambia.
- Refresh automático del access_token si vence a mitad de una acción (401 →
  refresca → reintenta una vez, sin que el usuario lo note) y manejo prolijo
  si el refresh_token fue revocado (queda en `AuthenticationFailed`, no
  reintenta solo — necesita reautorización).
- `Deck.Plugins.Spotify.Tests`: servidor Spotify falso propio (auth + Web API
  sobre `HttpListener`) que valida el PKCE de verdad (recomputa el challenge
  a partir del verifier recibido) — 11 tests cubriendo autorización, refresh,
  las 4 acciones de reproducción + volumen, expiración de token a mitad de
  sesión, y el evento de cambio de canción.

Siguiente: Fase 5 — Plugin #3: Discord (alcance por definir: ¿bot, webhook o Rich Presence?).

## Build y tests

```bash
dotnet build
dotnet test
```

Requiere .NET SDK 10.0.
