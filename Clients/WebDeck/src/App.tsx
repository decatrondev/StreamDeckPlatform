import { useEffect, useRef, useState } from "react"
import { ConnectScreen } from "./components/ConnectScreen"
import { DeckGrid } from "./components/DeckGrid"
import { StatusBar } from "./components/StatusBar"
import { api, ApiError, normalizeBaseUrl } from "./lib/api"
import { connectToDeckHub, type DeckHubHandle, type HubConnectionStatus } from "./lib/hub"
import type { Page, Plugin, Profile } from "./lib/types"

const STORAGE_KEY = "webdeck.serverUrl"

export default function App() {
  const [baseUrl, setBaseUrl] = useState<string | null>(() => localStorage.getItem(STORAGE_KEY))
  const [connectError, setConnectError] = useState<string | null>(null)

  const [profile, setProfile] = useState<Profile | null>(null)
  const [pageStack, setPageStack] = useState<Page[]>([])
  const [status, setStatus] = useState<HubConnectionStatus>("connecting")
  const [plugins, setPlugins] = useState<Plugin[]>([])
  const [pressedKey, setPressedKey] = useState<string | null>(null)
  const [failedKey, setFailedKey] = useState<string | null>(null)

  const hubRef = useRef<DeckHubHandle | null>(null)
  const currentPage = pageStack.at(-1) ?? null

  useEffect(() => {
    if (!baseUrl) return

    let cancelled = false

    async function boot(url: string) {
      try {
        const profiles = await api.getProfiles(url)
        const active = profiles[0]
        if (!active) throw new Error("El Core no tiene ningún perfil todavía.")

        const rootPage = await api.getPage(url, active.rootPageId)
        if (cancelled) return

        setProfile(active)
        setPageStack([rootPage])
        setConnectError(null)

        const refreshPlugins = () =>
          fetch(`${url}/api/plugins`)
            .then((r) => r.json())
            .then((p: Plugin[]) => !cancelled && setPlugins(p))
            .catch(() => {})

        refreshPlugins()
        hubRef.current = connectToDeckHub(url, setStatus, () => refreshPlugins())
        await hubRef.current.whenConnected
        if (cancelled) return
        hubRef.current.setActivePage(active.id, rootPage.id)
      } catch (err) {
        if (cancelled) return
        const message = err instanceof ApiError
          ? `No se pudo hablar con el Core (${err.status}). ¿La dirección es correcta?`
          : "No se pudo conectar. Revisá que Deck.Api esté corriendo en esa dirección."
        setConnectError(message)
        setBaseUrl(null)
      }
    }

    void boot(baseUrl)

    return () => {
      cancelled = true
      hubRef.current?.stop()
      hubRef.current = null
    }
  }, [baseUrl])

  function handleConnect(rawInput: string) {
    const normalized = normalizeBaseUrl(rawInput)
    if (!normalized) return
    localStorage.setItem(STORAGE_KEY, normalized)
    setBaseUrl(normalized)
  }

  function handleDisconnect() {
    hubRef.current?.stop()
    hubRef.current = null
    localStorage.removeItem(STORAGE_KEY)
    setBaseUrl(null)
    setProfile(null)
    setPageStack([])
  }

  async function handlePress(row: number, column: number) {
    if (!currentPage || !hubRef.current || !baseUrl) return

    const key = `${row}-${column}`
    setPressedKey(key)
    setTimeout(() => setPressedKey((k) => (k === key ? null : k)), 150)

    const result = await hubRef.current.executeButton(currentPage.id, row, column)

    if (result.navigatedToPageId) {
      const nextPage = await api.getPage(baseUrl, result.navigatedToPageId)
      setPageStack((stack) => [...stack, nextPage])
      if (profile) hubRef.current.setActivePage(profile.id, nextPage.id)
      return
    }

    if (!result.success) {
      setFailedKey(key)
      setTimeout(() => setFailedKey((k) => (k === key ? null : k)), 600)
    }
  }

  function handleBack() {
    if (pageStack.length <= 1) return
    const nextStack = pageStack.slice(0, -1)
    setPageStack(nextStack)
    const target = nextStack.at(-1)
    if (profile && target) hubRef.current?.setActivePage(profile.id, target.id)
  }

  if (!baseUrl) {
    return (
      <ConnectScreen
        initialValue={localStorage.getItem(STORAGE_KEY) ?? ""}
        onConnect={handleConnect}
        error={connectError}
      />
    )
  }

  if (!currentPage) {
    return (
      <div className="flex min-h-dvh items-center justify-center bg-graphite text-ink-muted">
        Cargando tu deck…
      </div>
    )
  }

  return (
    <div className="flex min-h-dvh flex-col bg-graphite">
      <StatusBar status={status} plugins={plugins} onDisconnect={handleDisconnect} />

      <div className="flex items-center gap-2 px-4 pt-4 3xl:px-8">
        {pageStack.length > 1 && (
          <button
            onClick={handleBack}
            className="flex size-7 items-center justify-center rounded-md text-ink-faint transition hover:bg-surface-raised hover:text-ink"
            aria-label="Volver"
          >
            ←
          </button>
        )}
        <p className="font-display text-sm font-medium text-ink-muted">
          {profile?.name} / {currentPage.name}
        </p>
      </div>

      <DeckGrid page={currentPage} pressedKey={pressedKey} failedKey={failedKey} onPress={handlePress} />
    </div>
  )
}
