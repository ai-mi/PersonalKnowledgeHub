import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { ItemPage } from './ItemPage'
import { ItemStatusBadge } from './ItemStatusBadge'
import type { Item } from './itemsApi'

const doc: Item = {
  id: 'd1',
  type: 'document',
  title: 'Report',
  url: null,
  status: 'pending',
  readAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  tags: [],
  body: null,
  userNote: null,
  codeLanguage: null,
  error: null,
  collections: [],
}

const server = setupServer(
  http.get('*/api/tags', () => HttpResponse.json([])),
  http.get('*/api/collections', () => HttpResponse.json([])),
)
beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const wrap = (ui: React.ReactNode) => (
  <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <MemoryRouter initialEntries={['/items/d1']}>{ui}</MemoryRouter>
  </QueryClientProvider>
)

it.each([
  ['pending', 'Processing'],
  ['processing', 'Processing'],
  ['ready', 'Ready'],
  ['failed', 'Error'],
])('labels status %s', (status, label) => {
  render(wrap(<ItemStatusBadge item={{ id: 'd1', status }} />))
  expect(screen.getByText(label)).toBeInTheDocument()
})

it('retries a failed item through the reprocess endpoint', async () => {
  let calls = 0
  server.use(
    http.post('*/api/items/d1/reprocess', () => {
      calls++
      return HttpResponse.json({ ...doc, status: 'pending' })
    }),
  )
  render(wrap(<ItemStatusBadge item={{ id: 'd1', status: 'failed', error: 'timeout' }} withRetry />))

  expect(screen.getByText(/timeout/)).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Retry' }))
  await waitFor(() => expect(calls).toBe(1))
})

it('polls a pending document until it is ready, then stops', async () => {
  let requests = 0
  server.use(
    http.get('*/api/items/d1', () => {
      requests++
      return HttpResponse.json({ ...doc, status: requests >= 2 ? 'ready' : 'processing', body: requests >= 2 ? 'Extracted text' : null })
    }),
  )
  render(
    wrap(
      <Routes>
        <Route path="/items/:id" element={<ItemPage />} />
      </Routes>,
    ),
  )

  expect(await screen.findByText('Processing')).toBeInTheDocument()
  expect(await screen.findByText('Extracted text', {}, { timeout: 5000 })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Download' })).toHaveAttribute('href', '/api/items/d1/file')

  const settled = requests
  await new Promise((r) => setTimeout(r, 2500))
  expect(requests).toBe(settled)
}, 15000)
