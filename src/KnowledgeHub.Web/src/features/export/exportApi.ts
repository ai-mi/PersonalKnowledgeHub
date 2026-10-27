import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../../api/client'

export type ExportStatus = 'pending' | 'processing' | 'ready' | 'failed'

export interface ExportRecord {
  id: string
  status: ExportStatus
  createdAt: string
  expiresAt: string | null
}

export const statusLabels: Record<ExportStatus, string> = {
  pending: 'В очереди',
  processing: 'Собирается',
  ready: 'Готов',
  failed: 'Ошибка',
}

const exportsKey = ['exports'] as const

export const isActive = (e: ExportRecord) => e.status === 'pending' || e.status === 'processing'

/** Пока есть незавершённый экспорт, список обновляется каждые `pollIntervalMs`. */
export function useExports(pollIntervalMs: number) {
  return useQuery({
    queryKey: exportsKey,
    queryFn: () => apiFetch<ExportRecord[]>('/api/exports'),
    refetchInterval: (query) => (query.state.data?.some(isActive) ? pollIntervalMs : false),
  })
}

export function useStartExport() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiFetch<ExportRecord>('/api/exports', { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: exportsKey }),
  })
}

export const downloadUrl = (id: string) => `/api/exports/${id}/download`
