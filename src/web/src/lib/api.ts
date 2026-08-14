import { ApiError, OpenAPI } from '../api/generated'
import { supabase } from './supabase'

OpenAPI.BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5080'
OpenAPI.TOKEN = async () => {
  const { data, error } = await supabase.auth.getSession()
  if (error || !data.session) {
    throw new Error('Tu sesión ya no está disponible.')
  }

  return data.session.access_token
}
OpenAPI.HEADERS = async () => ({ 'X-Correlation-ID': crypto.randomUUID() })

export function readableApiError(error: unknown): string {
  if (error instanceof ApiError && (error.status === 429 || error.status >= 500)) {
    return error.status === 429
      ? 'El servicio recibió demasiadas solicitudes. Espera un minuto e inténtalo de nuevo.'
      : 'El servicio gratuito puede estar despertando. Espera un momento e inténtalo de nuevo.'
  }
  if (error instanceof Error) return error.message
  return 'No pudimos completar la operación.'
}

export function shouldRetryApiQuery(failureCount: number, error: unknown): boolean {
  if (failureCount >= 7) return false
  return !(error instanceof ApiError) || error.status === 429 || error.status >= 500
}

export function apiRetryDelay(attempt: number): number {
  return Math.min(1_000 * 2 ** attempt, 10_000)
}
