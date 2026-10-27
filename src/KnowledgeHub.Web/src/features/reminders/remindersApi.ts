import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../../api/client'

export type ReminderChannel = 'telegram' | 'email' | 'both'

export const channelLabels: Record<ReminderChannel, string> = {
  telegram: 'Telegram',
  email: 'E-mail',
  both: 'Telegram и e-mail',
}

export interface Reminder {
  id: string
  itemId: string
  remindAt: string
  channel: ReminderChannel
  sentAt: string | null
}

export interface ReminderInput {
  itemId: string
  remindAt: string
  channel: ReminderChannel
}

const remindersKey = ['reminders'] as const

export function useItemReminders(itemId: string) {
  return useQuery({
    queryKey: [...remindersKey, 'item', itemId],
    queryFn: () => apiFetch<Reminder[]>(`/api/items/${itemId}/reminders`),
  })
}

export function useUpcomingReminders() {
  return useQuery({
    queryKey: [...remindersKey, 'upcoming'],
    queryFn: () => apiFetch<Reminder[]>('/api/reminders?upcoming=true'),
  })
}

export function useReminderMutations() {
  const queryClient = useQueryClient()
  const refresh = () => queryClient.invalidateQueries({ queryKey: remindersKey })
  return {
    create: useMutation({
      mutationFn: (input: ReminderInput) => apiFetch<Reminder>('/api/reminders', { method: 'POST', body: input }),
      onSuccess: refresh,
    }),
    remove: useMutation({
      mutationFn: (id: string) => apiFetch(`/api/reminders/${id}`, { method: 'DELETE' }),
      onSuccess: refresh,
    }),
  }
}

export function formatRemindAt(value: string, timeZone: string): string {
  return new Date(value).toLocaleString('ru-RU', { timeZone, dateStyle: 'short', timeStyle: 'short' })
}
