import { Link } from 'react-router'
import { useMe } from '../auth/useMe'
import { displayTitle, useItem } from '../items/itemsApi'
import { formatRemindAt, useUpcomingReminders, type Reminder } from './remindersApi'

function UpcomingRow({ reminder, timeZone }: { reminder: Reminder; timeZone: string }) {
  const { data: item } = useItem(reminder.itemId)
  return (
    <li>
      {formatRemindAt(reminder.remindAt, timeZone)} ·{' '}
      <Link to={`/items/${reminder.itemId}`}>{item ? displayTitle(item) : 'Запись'}</Link>
    </li>
  )
}

export function UpcomingReminders() {
  const { data: me } = useMe()
  const { data: reminders, error } = useUpcomingReminders()
  const timeZone = me?.settings.timezone ?? 'UTC'

  if (error) return <p role="alert">{error.message}</p>
  if (!reminders?.length) return null

  return (
    <section aria-label="Ближайшие напоминания">
      <h2>Ближайшие напоминания</h2>
      <ul>
        {reminders.map((r) => (
          <UpcomingRow key={r.id} reminder={r} timeZone={timeZone} />
        ))}
      </ul>
    </section>
  )
}
