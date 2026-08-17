import * as signalR from "@microsoft/signalr"
import type { ExecuteButtonResult, PluginEventMessage } from "./types"

export type HubConnectionStatus = "connecting" | "connected" | "reconnecting" | "disconnected"

export interface DeckHubHandle {
  connection: signalR.HubConnection
  whenConnected: Promise<void>
  executeButton: (pageId: string, row: number, column: number) => Promise<ExecuteButtonResult>
  setActivePage: (profileId: string, pageId: string) => void
  stop: () => Promise<void>
}

export function connectToDeckHub(
  baseUrl: string,
  pairingKey: string,
  onStatusChange: (status: HubConnectionStatus) => void,
  onPluginEvent: (event: PluginEventMessage) => void,
): DeckHubHandle {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${baseUrl}/hubs/deck?clientType=WebDeck`, {
      // Sin cookies de por medio: withCredentials=false matches la política
      // CORS permisiva del lado del servidor — un origin "*" con credenciales
      // incluidas, el browser lo rechaza directamente. El control de acceso
      // real es accessTokenFactory: el cliente JS de SignalR no puede setear
      // headers custom en el handshake de WebSocket, así que lo manda como
      // query string ?access_token=... (el servidor lo acepta ahí, ver
      // PairingKeyAuthenticationHandler).
      withCredentials: false,
      accessTokenFactory: () => pairingKey,
    })
    .withAutomaticReconnect([0, 1000, 3000, 5000, 5000])
    .build()

  connection.on("PluginEvent", (event: PluginEventMessage) => onPluginEvent(event))
  connection.onreconnecting(() => onStatusChange("reconnecting"))
  connection.onreconnected(() => onStatusChange("connected"))
  connection.onclose(() => onStatusChange("disconnected"))

  onStatusChange("connecting")
  const whenConnected = connection
    .start()
    .then(() => onStatusChange("connected"))
    .catch((err) => {
      onStatusChange("disconnected")
      throw err
    })

  return {
    connection,
    whenConnected,
    executeButton: (pageId, row, column) =>
      connection.invoke<ExecuteButtonResult>("ExecuteButton", pageId, row, column),
    setActivePage: (profileId, pageId) => {
      void connection.send("SetActivePage", profileId, pageId)
    },
    stop: () => connection.stop(),
  }
}
