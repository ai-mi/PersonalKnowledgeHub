import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type SubmitEvent } from 'react'
import { apiFetch } from '../../api/client'
import { meQueryKey, type Me, type Settings } from '../auth/useMe'

const frequencies = [
  { value: 'off', label: 'Off' },
  { value: 'daily', label: 'Daily' },
  { value: 'weekly', label: 'Weekly' },
]

const days = [
  { value: 'monday', label: 'Monday' },
  { value: 'tuesday', label: 'Tuesday' },
  { value: 'wednesday', label: 'Wednesday' },
  { value: 'thursday', label: 'Thursday' },
  { value: 'friday', label: 'Friday' },
  { value: 'saturday', label: 'Saturday' },
  { value: 'sunday', label: 'Sunday' },
]

const channels = [
  { value: 'telegram', label: 'Telegram' },
  { value: 'email', label: 'E-mail' },
]

export function DigestSection({ me }: { me: Me }) {
  const queryClient = useQueryClient()
  const [frequency, setFrequency] = useState(me.settings.digestFrequency)
  const [day, setDay] = useState(me.settings.digestDay)
  const [selected, setSelected] = useState(me.settings.digestChannels)
  const save = useMutation({
    mutationFn: () =>
      apiFetch<Settings>('/api/me/settings', {
        method: 'PUT',
        body: {
          timezone: me.settings.timezone,
          digestFrequency: frequency,
          digestDay: day,
          digestChannels: selected,
          // Адрес меняется только через подтверждение кодом (NotificationEmail); здесь — действующий.
          email: me.settings.email,
        },
      }),
    onSuccess: (settings) => queryClient.setQueryData<Me>(meQueryKey, { ...me, settings }),
  })

  const toggleChannel = (value: string, checked: boolean) =>
    setSelected((prev) => (checked ? [...prev, value] : prev.filter((c) => c !== value)))

  return (
    <section>
      <h2>Digest</h2>
      <p>The digest is sent at 9:00 in your time zone.</p>
      <form
        onSubmit={(e: SubmitEvent) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        <p>
          <label>
            Frequency{' '}
            <select value={frequency} onChange={(e) => setFrequency(e.target.value)}>
              {frequencies.map((f) => (
                <option key={f.value} value={f.value}>
                  {f.label}
                </option>
              ))}
            </select>
          </label>{' '}
          {frequency === 'weekly' && (
            <label>
              Day of week{' '}
              <select value={day} onChange={(e) => setDay(e.target.value)}>
                {days.map((d) => (
                  <option key={d.value} value={d.value}>
                    {d.label}
                  </option>
                ))}
              </select>
            </label>
          )}
        </p>
        <fieldset>
          <legend>Channels</legend>
          {channels.map((c) => (
            <label key={c.value}>
              <input type="checkbox" checked={selected.includes(c.value)} onChange={(e) => toggleChannel(c.value, e.target.checked)} />{' '}
              {c.label}{' '}
            </label>
          ))}
        </fieldset>
        <button type="submit" disabled={save.isPending}>
          Save digest
        </button>
        {save.isSuccess && <span role="status"> Saved</span>}
        {save.error && <p role="alert">{save.error.message}</p>}
      </form>
      <NotificationEmail me={me} />
    </section>
  )
}

/**
 * Адрес для дайджестов и напоминаний. Новый адрес начинает получать письма только после ввода кода, отправленного
 * на него; пустое поле — письма на адрес профиля.
 */
function NotificationEmail({ me }: { me: Me }) {
  const queryClient = useQueryClient()
  const [email, setEmail] = useState(me.settings.email ?? '')
  const [code, setCode] = useState('')
  // Последний ответ сервера — свежее, чем `me` из пропсов (родитель перерисуется из кэша позже или не перерисуется вовсе).
  const [latest, setLatest] = useState<Settings | null>(null)
  const pending = (latest ?? me.settings).pendingEmail ?? null
  const update = (settings: Settings) => {
    setLatest(settings)
    queryClient.setQueryData<Me>(meQueryKey, { ...me, settings })
  }

  const request = useMutation({
    mutationFn: () =>
      apiFetch<Settings>('/api/me/email', { method: 'POST', body: { email: email.trim() === '' ? null : email.trim() } }),
    onSuccess: (settings) => {
      setCode('')
      update(settings)
    },
  })
  const confirm = useMutation({
    mutationFn: () => apiFetch<Settings>('/api/me/email/confirm', { method: 'POST', body: { code: code.trim() } }),
    onSuccess: update,
  })

  return (
    <>
      <form
        onSubmit={(e: SubmitEvent) => {
          e.preventDefault()
          confirm.reset()
          request.mutate()
        }}
      >
        <p>
          <label>
            Notification e-mail{' '}
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder={me.email ?? ''} />
          </label>{' '}
          <button type="submit" disabled={request.isPending}>
            Save address
          </button>
          {request.isSuccess && !pending && <span role="status"> Address saved</span>}
        </p>
        {request.error && <p role="alert">{request.error.message}</p>}
      </form>
      {pending && (
        <form
          onSubmit={(e: SubmitEvent) => {
            e.preventDefault()
            confirm.mutate()
          }}
        >
          <p>
            Code sent to {pending}. Enter it to confirm this address; until then, e-mails go to the previous address.
          </p>
          <p>
            <label>
              Code from the e-mail{' '}
              <input inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} />
            </label>{' '}
            <button type="submit" disabled={confirm.isPending || code.trim() === ''}>
              Confirm
            </button>
          </p>
          {confirm.error && <p role="alert">{confirm.error.message}</p>}
        </form>
      )}
      {confirm.isSuccess && !pending && <p role="status">Address confirmed</p>}
    </>
  )
}
