import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import type { Me } from '../auth/useMe'
import { SettingsPage } from './SettingsPage'

const me: Me = {
  id: '1',
  email: 'a@b.c',
  displayName: 'Alice',
  settings: { timezone: 'Europe/Berlin', digestFrequency: 'off', digestDay: 'monday', digestChannels: [], email: null },
}
let current: Me = me

const server = setupServer(http.get('*/api/me/tokens', () => HttpResponse.json([])))

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  current = me
})
afterAll(() => server.close())

function renderPage() {
  server.use(http.get('*/api/me', () => HttpResponse.json(current)))
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <SettingsPage />
    </QueryClientProvider>,
  )
}

it('shows the profile and the tokens section', async () => {
  renderPage()

  expect(await screen.findByText('Имя: Alice')).toBeInTheDocument()
  expect(screen.getByText('E-mail: a@b.c')).toBeInTheDocument()
  expect(screen.getByLabelText('Часовой пояс')).toHaveValue('Europe/Berlin')
  expect(await screen.findByRole('heading', { name: 'API-токены' })).toBeInTheDocument()
})

it('shows a dash when the user has no e-mail, and keeps a non-standard timezone selectable', async () => {
  current = { ...me, email: null, settings: { ...me.settings, timezone: 'Custom/Zone' } }
  renderPage()

  expect(await screen.findByText('E-mail: —')).toBeInTheDocument()
  expect(screen.getByLabelText('Часовой пояс')).toHaveValue('Custom/Zone')
})

it('saves the timezone keeping the other settings', async () => {
  let body: unknown
  server.use(
    http.put('*/api/me/settings', async ({ request }) => {
      body = await request.json()
      return HttpResponse.json({ ...me.settings, timezone: 'Asia/Tokyo' })
    }),
  )
  renderPage()

  await userEvent.selectOptions(await screen.findByLabelText('Часовой пояс'), 'Asia/Tokyo')
  await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))

  expect(await screen.findByRole('status')).toHaveTextContent('Сохранено')
  expect(body).toEqual({ ...me.settings, timezone: 'Asia/Tokyo' })
})

it('shows a save error', async () => {
  server.use(http.put('*/api/me/settings', () => HttpResponse.json({ title: 'Bad zone' }, { status: 400 })))
  renderPage()

  await userEvent.click(await screen.findByRole('button', { name: 'Сохранить' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Bad zone')
  expect(screen.queryByRole('status')).not.toBeInTheDocument()
})
