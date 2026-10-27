import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { TelegramSection } from './TelegramSection'

let linked = false
let statusRequests = 0

const server = setupServer(
  http.get('*/api/me/telegram', () => {
    statusRequests++
    return HttpResponse.json({ linked, linkedAt: linked ? '2026-01-01T00:00:00Z' : null })
  }),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
  linked = false
  statusRequests = 0
})
afterAll(() => server.close())

function renderSection() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <TelegramSection pollIntervalMs={20} />
    </QueryClientProvider>,
  )
}

const linkResponse = () =>
  HttpResponse.json({ url: 'https://t.me/bot?start=abc', expiresAt: new Date(Date.now() + 600_000).toISOString() })

it('opens the t.me link, polls until linked and then stops polling', async () => {
  const open = vi.spyOn(window, 'open').mockReturnValue(null)
  server.use(http.post('*/api/me/telegram/link', linkResponse))
  renderSection()

  await userEvent.click(await screen.findByRole('button', { name: 'Привязать' }))
  expect(open).toHaveBeenCalledWith('https://t.me/bot?start=abc', '_blank', 'noopener,noreferrer')
  expect(await screen.findByRole('status')).toHaveTextContent('ждём подтверждения')

  await waitFor(() => expect(statusRequests).toBeGreaterThan(2)) // опрос идёт
  linked = true

  expect(await screen.findByRole('button', { name: 'Отвязать' })).toBeInTheDocument()
  expect(screen.queryByRole('status')).not.toBeInTheDocument()
  const requestsAtLink = statusRequests
  await new Promise((resolve) => setTimeout(resolve, 150))
  expect(statusRequests).toBeLessThanOrEqual(requestsAtLink + 1) // не больше одного запроса «в полёте»
})

it('does not poll before the link is requested', async () => {
  renderSection()

  await screen.findByRole('button', { name: 'Привязать' })
  await new Promise((resolve) => setTimeout(resolve, 100))
  expect(statusRequests).toBe(1)
})

it('stops polling when the link code has expired', async () => {
  vi.spyOn(window, 'open').mockReturnValue(null)
  server.use(
    http.post('*/api/me/telegram/link', () =>
      HttpResponse.json({ url: 'https://t.me/bot?start=abc', expiresAt: new Date(Date.now() + 100).toISOString() }),
    ),
  )
  renderSection()

  await userEvent.click(await screen.findByRole('button', { name: 'Привязать' }))
  await new Promise((resolve) => setTimeout(resolve, 300))
  const settled = statusRequests
  await new Promise((resolve) => setTimeout(resolve, 150))

  expect(statusRequests).toBe(settled)
})

it('unlinks only after confirmation', async () => {
  linked = true
  let unlinked = 0
  server.use(
    http.delete('*/api/me/telegram/link', () => {
      unlinked++
      linked = false
      return new HttpResponse(null, { status: 204 })
    }),
  )
  vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
  renderSection()

  const button = await screen.findByRole('button', { name: 'Отвязать' })
  await userEvent.click(button)
  expect(unlinked).toBe(0)

  await userEvent.click(button)
  expect(await screen.findByRole('button', { name: 'Привязать' })).toBeInTheDocument()
  expect(unlinked).toBe(1)
})

it('shows an error when the bot is not configured', async () => {
  server.use(http.post('*/api/me/telegram/link', () => HttpResponse.json({ title: 'Service Unavailable' }, { status: 503 })))
  renderSection()

  await userEvent.click(await screen.findByRole('button', { name: 'Привязать' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Service Unavailable')
})
