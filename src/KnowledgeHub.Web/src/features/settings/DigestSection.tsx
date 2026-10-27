import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type SubmitEvent } from 'react'
import { apiFetch } from '../../api/client'
import { meQueryKey, type Me, type Settings } from '../auth/useMe'

const frequencies = [
  { value: 'off', label: 'Выключен' },
  { value: 'daily', label: 'Каждый день' },
  { value: 'weekly', label: 'Раз в неделю' },
]

const days = [
  { value: 'monday', label: 'Понедельник' },
  { value: 'tuesday', label: 'Вторник' },
  { value: 'wednesday', label: 'Среда' },
  { value: 'thursday', label: 'Четверг' },
  { value: 'friday', label: 'Пятница' },
  { value: 'saturday', label: 'Суббота' },
  { value: 'sunday', label: 'Воскресенье' },
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
  const [email, setEmail] = useState(me.settings.email ?? '')
  const save = useMutation({
    mutationFn: () =>
      apiFetch<Settings>('/api/me/settings', {
        method: 'PUT',
        body: {
          ...me.settings,
          digestFrequency: frequency,
          digestDay: day,
          digestChannels: selected,
          email: email.trim() === '' ? null : email.trim(),
        },
      }),
    onSuccess: (settings) => queryClient.setQueryData<Me>(meQueryKey, { ...me, settings }),
  })

  const toggleChannel = (value: string, checked: boolean) =>
    setSelected((prev) => (checked ? [...prev, value] : prev.filter((c) => c !== value)))

  return (
    <section>
      <h2>Дайджест</h2>
      <p>Подборка приходит в 9:00 по вашему часовому поясу.</p>
      <form
        onSubmit={(e: SubmitEvent) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        <p>
          <label>
            Частота{' '}
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
              День недели{' '}
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
          <legend>Каналы</legend>
          {channels.map((c) => (
            <label key={c.value}>
              <input type="checkbox" checked={selected.includes(c.value)} onChange={(e) => toggleChannel(c.value, e.target.checked)} />{' '}
              {c.label}{' '}
            </label>
          ))}
        </fieldset>
        <p>
          <label>
            E-mail для уведомлений{' '}
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder={me.email ?? ''} />
          </label>
        </p>
        <button type="submit" disabled={save.isPending}>
          Сохранить дайджест
        </button>
        {save.isSuccess && <span role="status"> Сохранено</span>}
        {save.error && <p role="alert">{save.error.message}</p>}
      </form>
    </section>
  )
}
