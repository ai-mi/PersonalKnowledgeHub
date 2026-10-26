import { useQuery } from '@tanstack/react-query'
import { ApiError, apiFetch } from '../../api/client'

export interface Settings {
  timezone: string
  digestFrequency: string
  digestDay: string
  digestChannels: string[]
  email: string | null
}

export interface Me {
  id: string
  email: string | null
  displayName: string
  settings: Settings
}

export const meQueryKey = ['me'] as const

/** `null` — пользователь не вошёл (401). */
export async function fetchMe(): Promise<Me | null> {
  try {
    return await apiFetch<Me>('/api/me')
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null
    }
    throw error
  }
}

export function useMe() {
  return useQuery({ queryKey: meQueryKey, queryFn: fetchMe, staleTime: 60_000 })
}
