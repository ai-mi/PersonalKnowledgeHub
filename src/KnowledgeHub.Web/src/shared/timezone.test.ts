import { expect, it } from 'vitest'
import { fromZonedInputValue, toZonedInputValue } from './timezone'

it('converts local wall time of a zone to UTC', () => {
  expect(fromZonedInputValue('2026-06-10T09:00', 'Asia/Yekaterinburg').toISOString()).toBe('2026-06-10T04:00:00.000Z')
  expect(fromZonedInputValue('2026-06-10T09:00', 'UTC').toISOString()).toBe('2026-06-10T09:00:00.000Z')
})

it('accounts for daylight saving time', () => {
  expect(fromZonedInputValue('2026-01-15T09:00', 'America/New_York').toISOString()).toBe('2026-01-15T14:00:00.000Z')
  expect(fromZonedInputValue('2026-07-15T09:00', 'America/New_York').toISOString()).toBe('2026-07-15T13:00:00.000Z')
})

it('round-trips through the zone', () => {
  const date = new Date('2026-03-01T22:30:00Z')
  expect(toZonedInputValue(date, 'Asia/Tokyo')).toBe('2026-03-02T07:30')
  expect(fromZonedInputValue(toZonedInputValue(date, 'Asia/Tokyo'), 'Asia/Tokyo')).toEqual(date)
})
