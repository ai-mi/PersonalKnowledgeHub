import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { UpcomingReminders } from './UpcomingReminders'

const server = setupServer(
  http.get('*/api/me', () => HttpResponse.json({ id: '1', displayName: 'A', email: null, settings: { timezone: 'Asia/Yekaterinburg' } })),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderBlock() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <UpcomingReminders />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

it('lists upcoming reminders with item titles in the user timezone', async () => {
  server.use(
    http.get('*/api/reminders', ({ request }) => {
      expect(new URL(request.url).searchParams.get('upcoming')).toBe('true')
      return HttpResponse.json([{ id: 'r1', itemId: 'i1', remindAt: '2026-06-10T04:00:00Z', channel: 'telegram', sentAt: null }])
    }),
    http.get('*/api/items/i1', () => HttpResponse.json({ id: 'i1', title: 'Статья', url: null })),
  )
  renderBlock()

  expect(await screen.findByRole('link', { name: 'Статья' })).toHaveAttribute('href', '/items/i1')
  expect(screen.getByText(/10\.06\.2026, 09:00/)).toBeInTheDocument()
})

it('renders nothing when there are no reminders', async () => {
  server.use(http.get('*/api/reminders', () => HttpResponse.json([])))
  renderBlock()

  await new Promise((resolve) => setTimeout(resolve, 50))
  expect(screen.queryByRole('heading')).not.toBeInTheDocument()
})
