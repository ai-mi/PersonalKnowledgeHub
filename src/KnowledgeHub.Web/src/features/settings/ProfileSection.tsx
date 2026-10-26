import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type SubmitEvent } from 'react'
import { apiFetch } from '../../api/client'
import { meQueryKey, type Me, type Settings } from '../auth/useMe'

function timeZones(current: string): string[] {
  return [...new Set([current, ...Intl.supportedValuesOf('timeZone')])]
}

export function ProfileSection({ me }: { me: Me }) {
  const queryClient = useQueryClient()
  const [timezone, setTimezone] = useState(me.settings.timezone)
  const save = useMutation({
    mutationFn: () =>
      apiFetch<Settings>('/api/me/settings', { method: 'PUT', body: { ...me.settings, timezone } }),
    onSuccess: (settings) => queryClient.setQueryData<Me>(meQueryKey, { ...me, settings }),
  })

  return (
    <section>
      <h2>Профиль</h2>
      <p>Имя: {me.displayName}</p>
      <p>E-mail: {me.email ?? '—'}</p>
      <form
        onSubmit={(e: SubmitEvent) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        <label>
          Часовой пояс{' '}
          <select value={timezone} onChange={(e) => setTimezone(e.target.value)}>
            {timeZones(me.settings.timezone).map((zone) => (
              <option key={zone} value={zone}>
                {zone}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit" disabled={save.isPending}>
          Сохранить
        </button>
        {save.isSuccess && <span role="status"> Сохранено</span>}
        {save.error && <p role="alert">{save.error.message}</p>}
      </form>
    </section>
  )
}
