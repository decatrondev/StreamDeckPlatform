export type ButtonSlotType = "Action" | "Folder"

export interface ActionStep {
  order: number
  pluginId: string
  actionId: string
  parametersJson: string
}

export interface ButtonSlot {
  id: string
  pageId: string
  row: number
  column: number
  type: ButtonSlotType
  targetPageId: string | null
  label: string | null
  iconRef: string | null
  steps: ActionStep[]
}

export interface Page {
  id: string
  name: string
  rows: number
  columns: number
  buttons: ButtonSlot[]
}

export interface Profile {
  id: string
  name: string
  rootPageId: string
}

export type PluginState =
  | "Loaded" | "Initializing" | "Ready" | "Connecting" | "Connected" | "Disconnected" | "Faulted"

export interface PluginAction {
  id: string
  name: string
  description: string | null
}

export interface Plugin {
  id: string
  name: string
  version: string
  state: PluginState
  lastError: string | null
  actions: PluginAction[]
}

export interface ExecuteButtonResult {
  success: boolean
  navigatedToPageId: string | null
  stepResults: { success: boolean; message: string | null }[] | null
  error: string | null
}

export interface PluginEventMessage {
  pluginId: string
  eventId: string
  payloadJson: string
  occurredAt: string
}
