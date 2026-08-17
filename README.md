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
│   ├── Deck.Plugins.Spotify   → Fase 4, completo.
│   ├── Deck.Plugins.Discord   → Fase 5, completo.
│   └── Deck.Plugins.Twitch    → Fase 6, completo. MVP de plugins cerrado.
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

**Fase 5 — Plugin #3: Discord, completa.**

- Alcance resuelto: **RPC local del cliente de Discord** — mismo patrón
  arquitectónico que OBS (proceso local hablando un protocolo propio), pero
  transporte totalmente distinto: named pipe en Windows
  (`\\.\pipe\discord-ipc-N`), socket de dominio Unix en Linux/macOS
  (`$XDG_RUNTIME_DIR/discord-ipc-N`). Valida que el patrón de Fase 1 no asume
  ningún transporte en particular — ni WebSocket (OBS) ni HTTPS (Spotify).
- RPC no puede enviar mensajes de texto a un canal (fuera de su alcance) — se
  resolvió con un webhook opcional guardado vía Credential Manager, tercera
  forma de credencial distinta (contraseña simple → OAuth refresh_token →
  URL de webhook).
- Acciones: mutear/desmutear (toggle real, vía `GET_VOICE_SETTINGS` +
  `SET_VOICE_SETTINGS`), cambiar de canal de voz (`SELECT_VOICE_CHANNEL`),
  enviar mensaje rápido (webhook).
- Evento `voice-state-update` relayado como `PluginEvent`, más
  `connection-state` para reflejar conectado/reconectando en la UI.
- Reconexión automática propia ante cierre de Discord, mismo criterio que OBS.
- `Deck.Plugins.Discord.Tests`: servidor IPC falso propio (named pipe/socket
  Unix real, mismo framing binario que Discord) — 9 tests cubriendo handshake,
  toggle de mute, cambio de canal, comando rechazado, webhook configurado/sin
  configurar, relay de eventos y reconexión automática tras una caída.

**Fase 6 — Plugin #4: Twitch, completa. MVP de plugins cerrado.**

- OAuth Authorization Code + PKCE, mismo patrón que Spotify.
- **EventSub por WebSocket** (`wss://eventsub.wss.twitch.tv/ws`) — el plugin
  más exigente en tiempo real del MVP, tal como anticipaba el roadmap:
  handshake con `session_welcome` (entrega un `session_id` que hay que usar
  para dar de alta las suscripciones por REST, no alcanza con conectar),
  `session_keepalive` periódico, y una decisión consciente de simplificar
  `session_reconnect` tratándolo como una caída más en vez de migrar la
  sesión en caliente al `reconnect_url` específico (documentado en el propio
  código).
- El keepalive resultó clave: una caída de red sin frame de cierre (probada
  con `Abort()`, sin aviso prolijo) solo se nota gracias al watchdog de
  inactividad que arma el cliente — mismo tipo de problema que ya había
  aparecido con OBS en Fase 3, esta vez la propia Twitch lo vuelve parte
  explícita del protocolo en lugar de dejarlo en manos de TCP.
- Acciones: cambiar título, cambiar categoría, crear marcador, enviar mensaje
  al chat (vía Helix `/chat/messages`, no IRC).
- Eventos EventSub relayados como `PluginEvent`: `follow`, `subscribe`, `raid`.
- `Deck.Plugins.Twitch.Tests`: dos servidores falsos (Helix+OAuth por HTTP,
  EventSub por WebSocket) — 9 tests cubriendo PKCE, conexión y alta de las 3
  suscripciones, las 4 acciones, token vencido a mitad de sesión, relay de
  evento `follow`, y reconexión automática disparada por el timeout de
  keepalive ante una caída sin aviso.

Con esto se completan las Fases 0-6: Core, UI y los 4 plugins del MVP
(OBS, Spotify, Discord, Twitch). Siguiente: Fase 7 — API y Web Deck.

## Build y tests

```bash
dotnet build
dotnet test
```

Requiere .NET SDK 10.0.
