import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiFetch } from '../../api/client'

interface TelegramStatus {
  linked: boolean
  linkedAt: string | null
}

interface LinkCode {
  url: string
  expiresAt: string
}

const telegramKey = ['telegram'] as const
const POLL_INTERVAL_MS = 3000
const POLL_LIMIT_MS = 10 * 60 * 1000

export function TelegramSection({ pollIntervalMs = POLL_INTERVAL_MS }: { pollIntervalMs?: number }) {
  const queryClient = useQueryClient()
  // Момент, до которого опрашиваем статус после выдачи ссылки; `null` — не опрашиваем.
  const [pollUntil, setPollUntil] = useState<number | null>(null)

  const status = useQuery({
    queryKey: telegramKey,
    queryFn: () => apiFetch<TelegramStatus>('/api/me/telegram'),
    refetchInterval: (query) =>
      pollUntil !== null && query.state.data?.linked !== true && Date.now() < pollUntil ? pollIntervalMs : false,
  })

  const link = useMutation({
    mutationFn: () => apiFetch<LinkCode>('/api/me/telegram/link', { method: 'POST' }),
    onSuccess: ({ url, expiresAt }) => {
      window.open(url, '_blank', 'noopener,noreferrer')
      setPollUntil(Math.min(Date.now() + POLL_LIMIT_MS, new Date(expiresAt).getTime()))
    },
  })
  const unlink = useMutation({
    mutationFn: () => apiFetch('/api/me/telegram/link', { method: 'DELETE' }),
    onSuccess: () => {
      setPollUntil(null)
      return queryClient.invalidateQueries({ queryKey: telegramKey })
    },
  })

  const waiting = pollUntil !== null && status.data?.linked === false && Date.now() < pollUntil

  return (
    <section>
      <h2>Telegram</h2>
      {status.error && <p role="alert">{status.error.message}</p>}
      {status.data?.linked ? (
        <p>
          Привязан{status.data.linkedAt && ` ${new Date(status.data.linkedAt).toLocaleDateString('ru-RU')}`}.{' '}
          <button
            type="button"
            disabled={unlink.isPending}
            onClick={() => {
              if (window.confirm('Отвязать Telegram?')) unlink.mutate()
            }}
          >
            Отвязать
          </button>
        </p>
      ) : (
        status.data && (
          <p>
            Не привязан.{' '}
            <button type="button" disabled={link.isPending} onClick={() => link.mutate()}>
              Привязать
            </button>
          </p>
        )
      )}
      {waiting && <p role="status">Откройте бота в Telegram и нажмите «Start» — ждём подтверждения…</p>}
      {link.error && <p role="alert">{link.error.message}</p>}
      {unlink.error && <p role="alert">{unlink.error.message}</p>}
    </section>
  )
}
