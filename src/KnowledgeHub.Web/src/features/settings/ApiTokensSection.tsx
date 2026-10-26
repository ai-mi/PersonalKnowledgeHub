import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type SubmitEvent } from 'react'
import { apiFetch } from '../../api/client'

interface TokenInfo {
  id: string
  name: string
  createdAt: string
  expiresAt: string | null
  revokedAt: string | null
  lastUsedAt: string | null
}

interface CreatedToken {
  id: string
  name: string
  token: string
}

const tokensQueryKey = ['tokens'] as const

function formatDate(value: string | null): string {
  return value === null ? '—' : new Date(value).toLocaleDateString('ru-RU')
}

export function ApiTokensSection() {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [expiresInDays, setExpiresInDays] = useState('')
  const [created, setCreated] = useState<CreatedToken | null>(null)
  const [copied, setCopied] = useState(false)

  const tokens = useQuery({
    queryKey: tokensQueryKey,
    queryFn: () => apiFetch<TokenInfo[]>('/api/me/tokens'),
  })
  const refresh = () => queryClient.invalidateQueries({ queryKey: tokensQueryKey })

  const create = useMutation({
    mutationFn: () =>
      apiFetch<CreatedToken>('/api/me/tokens', {
        method: 'POST',
        body: { name, expiresInDays: expiresInDays === '' ? null : Number(expiresInDays) },
      }),
    onSuccess: async (token) => {
      setCreated(token)
      setCopied(false)
      setName('')
      setExpiresInDays('')
      await refresh()
    },
  })
  const revoke = useMutation({
    mutationFn: (id: string) => apiFetch(`/api/me/tokens/${id}`, { method: 'DELETE' }),
    onSuccess: refresh,
  })

  const copy = async (token: string) => {
    await navigator.clipboard.writeText(token)
    setCopied(true)
  }

  const confirmRevoke = (token: TokenInfo) => {
    if (window.confirm(`Отозвать токен «${token.name}»? Это действие нельзя отменить.`)) {
      revoke.mutate(token.id)
    }
  }

  return (
    <section>
      <h2>API-токены</h2>

      {created && (
        <div role="alert">
          <p>
            Токен «{created.name}» создан. Скопируйте его сейчас — позже он показан не будет.
          </p>
          <code>{created.token}</code>{' '}
          <button type="button" onClick={() => void copy(created.token)}>
            {copied ? 'Скопировано' : 'Скопировать'}
          </button>{' '}
          <button type="button" onClick={() => setCreated(null)}>
            Закрыть
          </button>
        </div>
      )}

      <form
        onSubmit={(e: SubmitEvent) => {
          e.preventDefault()
          create.mutate()
        }}
      >
        <label>
          Название <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} required />
        </label>{' '}
        <label>
          Срок, дней{' '}
          <input
            type="number"
            min={1}
            value={expiresInDays}
            onChange={(e) => setExpiresInDays(e.target.value)}
            placeholder="без срока"
          />
        </label>{' '}
        <button type="submit" disabled={create.isPending}>
          Создать токен
        </button>
        {create.error && <p role="alert">{create.error.message}</p>}
      </form>

      {tokens.isPending && <p>Загрузка…</p>}
      {tokens.error && <p role="alert">{tokens.error.message}</p>}
      {tokens.data?.length === 0 && <p>Токенов пока нет.</p>}
      {tokens.data && tokens.data.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Название</th>
              <th>Создан</th>
              <th>Истекает</th>
              <th>Использован</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {tokens.data.map((token) => (
              <tr key={token.id}>
                <td>{token.name}</td>
                <td>{formatDate(token.createdAt)}</td>
                <td>{formatDate(token.expiresAt)}</td>
                <td>{formatDate(token.lastUsedAt)}</td>
                <td>
                  {token.revokedAt ? (
                    'Отозван'
                  ) : (
                    <button type="button" onClick={() => confirmRevoke(token)}>
                      Отозвать
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {revoke.error && <p role="alert">{revoke.error.message}</p>}
    </section>
  )
}
