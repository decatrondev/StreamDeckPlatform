import { useState } from "react"

interface Props {
  initialServerUrl: string
  initialPairingKey: string
  onConnect: (baseUrl: string, pairingKey: string) => void
  error: string | null
}

export function ConnectScreen({ initialServerUrl, initialPairingKey, onConnect, error }: Props) {
  const [serverUrl, setServerUrl] = useState(initialServerUrl)
  const [pairingKey, setPairingKey] = useState(initialPairingKey)

  return (
    <div className="flex min-h-dvh items-center justify-center bg-graphite px-6">
      <div className="w-full max-w-md">
        <div className="mb-8 flex items-center gap-3">
          <div className="grid size-9 grid-cols-2 gap-0.5 rounded-lg bg-surface-raised p-1.5">
            <span className="rounded-sm bg-accent" />
            <span className="rounded-sm bg-line" />
            <span className="rounded-sm bg-line" />
            <span className="rounded-sm bg-signal" />
          </div>
          <div>
            <p className="font-display text-lg font-semibold text-ink">Web Deck</p>
            <p className="text-xs text-ink-faint">Flowdeck</p>
          </div>
        </div>

        <h1 className="mb-2 font-display text-2xl font-semibold text-ink">Conectar con tu Core</h1>
        <p className="mb-6 text-sm leading-relaxed text-ink-muted">
          Escribí la dirección de la máquina donde corre Deck.Api — la misma red local
          que tu compu, sin instalar nada acá. La pairing key aparece en la consola de
          Deck.Api al arrancarlo, o en el archivo{" "}
          <code className="font-mono text-ink-muted">pairing.key</code>.
        </p>

        <form
          onSubmit={(e) => {
            e.preventDefault()
            onConnect(serverUrl, pairingKey)
          }}
          className="flex flex-col gap-3"
        >
          <input
            autoFocus
            value={serverUrl}
            onChange={(e) => setServerUrl(e.target.value)}
            placeholder="192.168.1.10:5210"
            className="rounded-lg border border-line bg-surface px-4 py-3 font-mono text-sm text-ink outline-none placeholder:text-ink-faint focus:border-accent"
          />
          <input
            value={pairingKey}
            onChange={(e) => setPairingKey(e.target.value)}
            placeholder="pairing key"
            type="password"
            className="rounded-lg border border-line bg-surface px-4 py-3 font-mono text-sm text-ink outline-none placeholder:text-ink-faint focus:border-accent"
          />
          <button
            type="submit"
            className="rounded-lg bg-accent px-4 py-3 font-display text-sm font-semibold text-white transition hover:bg-accent-light"
          >
            Conectar
          </button>
        </form>

        {error && (
          <p className="mt-4 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {error}
          </p>
        )}
      </div>
    </div>
  )
}
