import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import { ReminderPicker } from './ReminderPicker'

const me = {
  id: '1',
  displayName: 'A',
  email: null,
  settings: { timezone: 'Asia/Yekaterinburg', digestFrequency: 'off', digestDay: 'monday', digestChannels: [], email: null },
}
let reminders: unknown[] = []

const server = setupServer(
  http.get('*/api/me', () => HttpResponse.json(me)),
  http.get('*/api/items/i1/reminders', () => HttpResponse.json(reminders)),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
beforeEach(() => {
  // Только Date: таймеры нужны msw и React Query.
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-06-01T00:00:00Z'))
})
afterEach(() => {
  vi.useRealTimers()
  server.resetHandlers()
  reminders = []
})
afterAll(() => server.close())

function renderPicker() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ReminderPicker itemId="i1" />
    </QueryClientProvider>,
  )
}

const pickDate = async (value: string) => {
  const input = await screen.findByLabelText(/Дата и время/)
  fireEvent.change(input, { target: { value } })
}

it('sends the time chosen in the user timezone as UTC', async () => {
  let body: unknown
  server.use(
    http.post('*/api/reminders', async ({ request }) => {
      body = await request.json()
      return HttpResponse.json({}, { status: 201 })
    }),
  )
  renderPicker()

  await pickDate('2026-06-10T09:00')
  await userEvent.selectOptions(screen.getByLabelText('Канал'), 'both')
  await userEvent.click(screen.getByRole('button', { name: 'Напомнить' }))

  await waitFor(() => expect(body).toEqual({ itemId: 'i1', remindAt: '2026-06-10T04:00:00.000Z', channel: 'both' }))
})

it('does not allow a time in the past', async () => {
  renderPicker()

  await pickDate('2026-06-01T04:00') // 04:00 в Екатеринбурге = 23:00 UTC накануне, раньше «сейчас»
  expect(await screen.findByRole('alert')).toHaveTextContent('Это время уже прошло.')
  expect(screen.getByRole('button', { name: 'Напомнить' })).toBeDisabled()
})

it('fills quick options at 9:00 local time', async () => {
  let body: unknown
  server.use(
    http.post('*/api/reminders', async ({ request }) => {
      body = await request.json()
      return HttpResponse.json({}, { status: 201 })
    }),
  )
  renderPicker()

  await userEvent.click(await screen.findByRole('button', { name: 'Через неделю' }))
  expect(screen.getByLabelText(/Дата и время/)).toHaveValue('2026-06-08T09:00')
  await userEvent.click(screen.getByRole('button', { name: 'Через месяц' }))
  expect(screen.getByLabelText(/Дата и время/)).toHaveValue('2026-07-01T09:00')
  await userEvent.click(screen.getByRole('button', { name: 'Завтра' }))
  await userEvent.click(screen.getByRole('button', { name: 'Напомнить' }))

  await waitFor(() => expect(body).toMatchObject({ remindAt: '2026-06-02T04:00:00.000Z', channel: 'telegram' }))
})

it('lists reminders of the item and deletes one', async () => {
  reminders = [{ id: 'r1', itemId: 'i1', remindAt: '2026-06-10T04:00:00Z', channel: 'email', sentAt: null }]
  let deleted = 0
  server.use(
    http.delete('*/api/reminders/r1', () => {
      deleted++
      reminders = []
      return new HttpResponse(null, { status: 204 })
    }),
  )
  renderPicker()

  expect(await screen.findByText(/10\.06\.2026.*E-mail/)).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Удалить напоминание' }))

  expect(await screen.findByText('Напоминаний нет.')).toBeInTheDocument()
  expect(deleted).toBe(1)
})

it('shows a server error on create', async () => {
  server.use(http.post('*/api/reminders', () => HttpResponse.json({ title: 'Nope' }, { status: 400 })))
  renderPicker()

  await pickDate('2026-06-10T09:00')
  await userEvent.click(screen.getByRole('button', { name: 'Напомнить' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Nope')
})
