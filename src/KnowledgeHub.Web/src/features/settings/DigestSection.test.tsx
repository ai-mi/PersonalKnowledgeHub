import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import type { Me } from '../auth/useMe'
import { DigestSection } from './DigestSection'

const me: Me = {
  id: '1',
  email: 'a@b.c',
  displayName: 'Alice',
  settings: { timezone: 'Europe/Berlin', digestFrequency: 'off', digestDay: 'monday', digestChannels: [], email: null },
}

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderSection() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <DigestSection me={me} />
    </QueryClientProvider>,
  )
}

it('saves frequency, day, channels and e-mail keeping the other settings', async () => {
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
  await userEvent.type(screen.getByLabelText('E-mail для уведомлений'), 'x@y.z')
  await userEvent.click(screen.getByRole('button', { name: 'Сохранить дайджест' }))

  expect(await screen.findByRole('status')).toHaveTextContent('Сохранено')
  expect(body).toEqual({
    timezone: 'Europe/Berlin',
    digestFrequency: 'weekly',
    digestDay: 'friday',
    digestChannels: ['email'],
    email: 'x@y.z',
  })
})

it('sends a null e-mail when the field is empty and shows a save error', async () => {
  let body: { email: unknown } | undefined
  server.use(
    http.put('*/api/me/settings', async ({ request }) => {
      body = (await request.json()) as { email: unknown }
      return HttpResponse.json({ title: 'Invalid e-mail.' }, { status: 400 })
    }),
  )
  renderSection()

  await userEvent.click(screen.getByRole('button', { name: 'Сохранить дайджест' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Invalid e-mail.')
  expect(body?.email).toBeNull()
})
