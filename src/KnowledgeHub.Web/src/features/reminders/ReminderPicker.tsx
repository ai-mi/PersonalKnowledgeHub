import { useState, type SubmitEvent } from 'react'
import { fromZonedInputValue, toZonedInputValue } from '../../shared/timezone'
import { useMe } from '../auth/useMe'
import { channelLabels, formatRemindAt, useItemReminders, useReminderMutations, type ReminderChannel } from './remindersApi'

const REMINDER_HOUR = '09'

/** Быстрый вариант: сдвиг от сегодняшней даты пользователя, время — 9:00 по его часам. */
function quickValue(timeZone: string, shift: { days?: number; months?: number }): string {
  const [y, mo, d] = toZonedInputValue(new Date(), timeZone).slice(0, 10).split('-').map(Number)
  const date = new Date(Date.UTC(y, mo - 1 + (shift.months ?? 0), d + (shift.days ?? 0)))
  return `${date.toISOString().slice(0, 10)}T${REMINDER_HOUR}:00`
}

const quickOptions = [
  { label: 'Завтра', shift: { days: 1 } },
  { label: 'Через неделю', shift: { days: 7 } },
  { label: 'Через месяц', shift: { months: 1 } },
]

export function ReminderPicker({ itemId }: { itemId: string }) {
  const { data: me } = useMe()
  const timeZone = me?.settings.timezone ?? 'UTC'
  const [value, setValue] = useState('')
  const [channel, setChannel] = useState<ReminderChannel>('telegram')
  const reminders = useItemReminders(itemId)
  const { create, remove } = useReminderMutations()

  const remindAt = value ? fromZonedInputValue(value, timeZone) : null
  const inPast = remindAt !== null && remindAt.getTime() <= Date.now()

  return (
    <section>
      <h2>Напоминания</h2>
      {reminders.error && <p role="alert">{reminders.error.message}</p>}
      {reminders.data?.length === 0 && <p>Напоминаний нет.</p>}
      <ul>
        {reminders.data?.map((r) => (
          <li key={r.id}>
            {formatRemindAt(r.remindAt, timeZone)} · {channelLabels[r.channel]}
            {r.sentAt && ' · отправлено'}{' '}
            <button type="button" onClick={() => remove.mutate(r.id)} disabled={remove.isPending}>
              Удалить напоминание
            </button>
          </li>
        ))}
      </ul>
      <form
        onSubmit={(e: SubmitEvent) => {
          e.preventDefault()
          if (remindAt === null || inPast) return
          create.mutate({ itemId, remindAt: remindAt.toISOString(), channel }, { onSuccess: () => setValue('') })
        }}
      >
        <label>
          Дата и время ({timeZone}){' '}
          <input
            type="datetime-local"
            value={value}
            min={toZonedInputValue(new Date(), timeZone)}
            onChange={(e) => setValue(e.target.value)}
          />
        </label>{' '}
        {quickOptions.map((option) => (
          <button key={option.label} type="button" onClick={() => setValue(quickValue(timeZone, option.shift))}>
            {option.label}
          </button>
        ))}{' '}
        <label>
          Канал{' '}
          <select value={channel} onChange={(e) => setChannel(e.target.value as ReminderChannel)}>
            {Object.entries(channelLabels).map(([key, label]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit" disabled={remindAt === null || inPast || create.isPending}>
          Напомнить
        </button>
        {inPast && <p role="alert">Это время уже прошло.</p>}
        {create.error && <p role="alert">{create.error.message}</p>}
        {remove.error && <p role="alert">{remove.error.message}</p>}
      </form>
    </section>
  )
}
