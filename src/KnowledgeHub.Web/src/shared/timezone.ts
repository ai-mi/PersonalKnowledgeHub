const pad = (n: number) => String(n).padStart(2, '0')

function zonedParts(date: Date, timeZone: string): number[] {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: 'numeric',
    second: 'numeric',
  }).formatToParts(date)
  return ['year', 'month', 'day', 'hour', 'minute', 'second'].map((type) => Number(parts.find((p) => p.type === type)?.value))
}

/** Смещение часового пояса от UTC (мс) в заданный момент. */
function offsetMs(date: Date, timeZone: string): number {
  const [y, mo, d, h, mi, s] = zonedParts(date, timeZone)
  return Date.UTC(y, mo - 1, d, h, mi, s) - Math.floor(date.getTime() / 1000) * 1000
}

/** Значение для `<input type="datetime-local">` (`YYYY-MM-DDTHH:mm`) — момент `date` по часам пояса `timeZone`. */
export function toZonedInputValue(date: Date, timeZone: string): string {
  const [y, mo, d, h, mi] = zonedParts(date, timeZone)
  return `${y}-${pad(mo)}-${pad(d)}T${pad(h)}:${pad(mi)}`
}

/** Обратное преобразование: локальное время пояса `timeZone` → момент в UTC. */
export function fromZonedInputValue(value: string, timeZone: string): Date {
  const [y, mo, d, h, mi] = value.split(/[-T:]/).map(Number)
  const wall = Date.UTC(y, mo - 1, d, h, mi)
  const first = wall - offsetMs(new Date(wall), timeZone)
  // Второй проход нужен рядом с переводом часов: смещение в найденный момент может отличаться.
  return new Date(wall - offsetMs(new Date(first), timeZone))
}
