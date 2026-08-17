import type { HubConnectionStatus } from "../lib/hub"
import type { Plugin } from "../lib/types"

interface Props {
  status: HubConnectionStatus
  plugins: Plugin[]
  onDisconnect: () => void
}

const STATUS_LABEL: Record<HubConnectionStatus, string> = {
  connecting: "Conectando…",
  connected: "En vivo",
  reconnecting: "Reconectando…",
  disconnected: "Sin conexión",
}

const STATUS_DOT: Record<HubConnectionStatus, string> = {
  connecting: "bg-signal animate-pulse",
  connected: "bg-live",
  reconnecting: "bg-signal animate-pulse",
  disconnected: "bg-danger",
}

export function StatusBar({ status, plugins, onDisconnect }: Props) {
  return (
    <div className="flex items-center justify-between border-b border-line bg-surface px-4 py-2.5 3xl:px-8">
      <div className="flex items-center gap-2">
        <span className={`size-2 rounded-full ${STATUS_DOT[status]}`} />
        <span className="text-xs font-medium text-ink-muted">{STATUS_LABEL[status]}</span>
      </div>

      <div className="hidden items-center gap-1.5 sm:flex">
        {plugins.map((plugin) => (
          <span
            key={plugin.id}
            title={`${plugin.name}: ${plugin.state}${plugin.lastError ? ` — ${plugin.lastError}` : ""}`}
            className={`rounded-full px-2 py-0.5 font-mono text-[10px] uppercase tracking-wide ${
              plugin.state === "Connected"
                ? "bg-live/15 text-live"
                : plugin.state === "Faulted"
                  ? "bg-danger/15 text-danger"
                  : "bg-line text-ink-faint"
            }`}
          >
            {plugin.name}
          </span>
        ))}
      </div>

      <button
        onClick={onDisconnect}
        className="text-xs font-medium text-ink-faint transition hover:text-ink"
      >
        Cambiar servidor
      </button>
    </div>
  )
}
