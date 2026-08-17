import type { Page, Profile } from "./types"

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(baseUrl: string, path: string): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`)
  if (!response.ok) {
    throw new ApiError(response.status, `${response.status} ${response.statusText} — ${path}`)
  }
  return (await response.json()) as T
}

export const api = {
  getProfiles: (baseUrl: string) => request<Profile[]>(baseUrl, "/api/profiles"),
  getPage: (baseUrl: string, pageId: string) => request<Page>(baseUrl, `/api/pages/${pageId}`),
}

// Normaliza lo que el usuario tipea ("192.168.1.10:5210", "localhost:5210/",
// etc.) a una base URL http utilizable — nadie en la vida real va a escribir
// el esquema completo desde el celular.
export function normalizeBaseUrl(input: string): string {
  const trimmed = input.trim().replace(/\/+$/, "")
  if (!trimmed) return trimmed
  return /^https?:\/\//i.test(trimmed) ? trimmed : `http://${trimmed}`
}
