import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import type { Me, Settings } from '../auth/useMe'
import { DigestSection } from './DigestSection'

const settings: Settings = {
  timezone: 'Europe/Berlin',
  digestFrequency: 'off',
  digestDay: 'monday',
  digestChannels: [],
  email: 'old@b.c',
  pendingEmail: null,
}

const me: Me = { id: '1', email: 'a@b.c', displayName: 'Alice', settings }

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderSection(current: Me = me) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <DigestSection me={current} />
    </QueryClientProvider>,
  )
}

it('saves frequency, day and channels keeping the other settings and the confirmed address', async () => {
  let body: unknown
  server.use(
    http.put('*/api/me/settings', async ({ request }) => {
      body = await request.json()
      return HttpResponse.json(body as Record<string, unknown>)
    }),
  )
  renderSection()

  expect(screen.queryByLabelText('День недели')).not.toBeInTheDocument()
  await userEvent.selectOptions(screen.getByLabelText('Частота'), 'weekly')
  await userEvent.selectOptions(screen.getByLabelText('День недели'), 'friday')
  await userEvent.click(screen.getByRole('checkbox', { name: 'Telegram' }))
  await userEvent.click(screen.getByRole('checkbox', { name: 'E-mail' }))
  await userEvent.click(screen.getByRole('checkbox', { name: 'Telegram' }))
  // Адрес в поле, но не сохранён своей кнопкой: сохранение дайджеста его не меняет.
  await userEvent.clear(screen.getByLabelText('E-mail для уведомлений'))
  await userEvent.type(screen.getByLabelText('E-mail для уведомлений'), 'x@y.z')
  await userEvent.click(screen.getByRole('button', { name: 'Сохранить дайджест' }))

  expect(await screen.findByText('Сохранено')).toBeInTheDocument()
  expect(body).toEqual({
    timezone: 'Europe/Berlin',
    digestFrequency: 'weekly',
    digestDay: 'friday',
    digestChannels: ['email'],
    email: 'old@b.c',
  })
})

it('shows a digest save error', async () => {
  server.use(http.put('*/api/me/settings', () => HttpResponse.json({ title: 'Unknown IANA time zone.' }, { status: 400 })))
  renderSection()

  await userEvent.click(screen.getByRole('button', { name: 'Сохранить дайджест' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Unknown IANA time zone.')
})

it('sends a code to a new address and switches to it after the code is confirmed', async () => {
  let requested: unknown
  let confirmed: unknown
  server.use(
    http.post('*/api/me/email', async ({ request }) => {
      requested = await request.json()
      return HttpResponse.json({ ...settings, pendingEmail: 'x@y.z' })
    }),
    http.post('*/api/me/email/confirm', async ({ request }) => {
      confirmed = await request.json()
      return HttpResponse.json({ ...settings, email: 'x@y.z', pendingEmail: null })
    }),
  )
  renderSection()

  await userEvent.clear(screen.getByLabelText('E-mail для уведомлений'))
  await userEvent.type(screen.getByLabelText('E-mail для уведомлений'), ' x@y.z ')
  await userEvent.click(screen.getByRole('button', { name: 'Сохранить адрес' }))

  expect(await screen.findByText(/Код отправлен на x@y\.z/)).toBeInTheDocument()
  expect(requested).toEqual({ email: 'x@y.z' })

  await userEvent.type(screen.getByLabelText('Код из письма'), '123456')
  await userEvent.click(screen.getByRole('button', { name: 'Подтвердить' }))

  expect(await screen.findByText('Адрес подтверждён')).toBeInTheDocument()
  expect(confirmed).toEqual({ code: '123456' })
  expect(screen.queryByLabelText('Код из письма')).not.toBeInTheDocument()
})

it('sends null for an empty address (mail goes to the profile address)', async () => {
  let requested: unknown
  server.use(
    http.post('*/api/me/email', async ({ request }) => {
      requested = await request.json()
      return HttpResponse.json({ ...settings, email: null })
    }),
  )
  renderSection()

  await userEvent.clear(screen.getByLabelText('E-mail для уведомлений'))
  await userEvent.click(screen.getByRole('button', { name: 'Сохранить адрес' }))

  expect(await screen.findByText('Адрес сохранён')).toBeInTheDocument()
  expect(requested).toEqual({ email: null })
})

it('asks for the code right away when an address is already awaiting confirmation', () => {
  renderSection({ ...me, settings: { ...settings, pendingEmail: 'p@q.r' } })

  expect(screen.getByText(/Код отправлен на p@q\.r/)).toBeInTheDocument()
  expect(screen.getByLabelText('Код из письма')).toBeInTheDocument()
})

it('shows address and code errors', async () => {
  server.use(
    http.post('*/api/me/email', () => HttpResponse.json({ title: 'Too Many Requests' }, { status: 429 })),
    http.post('*/api/me/email/confirm', () => HttpResponse.json({ title: 'Invalid code.' }, { status: 400 })),
  )
  renderSection({ ...me, settings: { ...settings, pendingEmail: 'p@q.r' } })

  await userEvent.click(screen.getByRole('button', { name: 'Сохранить адрес' }))
  expect(await screen.findByText('Too Many Requests')).toBeInTheDocument()

  await userEvent.type(screen.getByLabelText('Код из письма'), '000000')
  await userEvent.click(screen.getByRole('button', { name: 'Подтвердить' }))
  expect(await screen.findByText('Invalid code.')).toBeInTheDocument()
})
