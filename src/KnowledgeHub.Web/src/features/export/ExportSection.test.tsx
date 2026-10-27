import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import type { ExportRecord } from './exportApi'
import { ExportSection } from './ExportSection'

let exports: ExportRecord[] = []
let listRequests = 0
let startRequests = 0

const server = setupServer(
  http.get('*/api/exports', () => {
    listRequests++
    return HttpResponse.json(exports)
  }),
  http.post('*/api/exports', () => {
    startRequests++
    const created: ExportRecord = { id: 'new', status: 'pending', createdAt: '2026-10-27T10:00:00Z', expiresAt: null }
    exports = [created, ...exports]
    return HttpResponse.json(created, { status: 202 })
  }),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  exports = []
  listRequests = 0
  startRequests = 0
})
afterAll(() => server.close())

function renderSection() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ExportSection pollIntervalMs={20} />
    </QueryClientProvider>,
  )
}

it('starts an export, disables the button while it runs and offers the download when ready', async () => {
  renderSection()
  const button = await screen.findByRole('button', { name: 'Экспортировать' })
  expect(button).toBeEnabled()

  await userEvent.click(button)

  await waitFor(() => expect(screen.getByRole('button', { name: 'Экспортировать' })).toBeDisabled())
  expect(await screen.findByRole('status')).toHaveTextContent('Архив собирается')
  expect(startRequests).toBe(1)

  exports = [{ id: 'new', status: 'ready', createdAt: '2026-10-27T10:00:00Z', expiresAt: '2026-11-03T10:00:00Z' }]

  const link = await screen.findByRole('link', { name: 'Скачать' })
  expect(link).toHaveAttribute('href', '/api/exports/new/download')
  expect(screen.getByRole('button', { name: 'Экспортировать' })).toBeEnabled()
  expect(screen.queryByRole('status')).not.toBeInTheDocument()
})

it('keeps the button disabled when an export is already in progress and polls only while it is', async () => {
  exports = [{ id: 'a', status: 'processing', createdAt: '2026-10-27T10:00:00Z', expiresAt: null }]
  renderSection()

  await waitFor(() => expect(screen.getByRole('button', { name: 'Экспортировать' })).toBeDisabled())
  await waitFor(() => expect(listRequests).toBeGreaterThan(2))

  exports = [{ id: 'a', status: 'failed', createdAt: '2026-10-27T10:00:00Z', expiresAt: null }]
  expect(await screen.findByText(/Ошибка/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Экспортировать' })).toBeEnabled()
  const settled = listRequests
  await new Promise((resolve) => setTimeout(resolve, 150))
  expect(listRequests).toBeLessThanOrEqual(settled + 1)
})

it('shows the error when the export cannot be started', async () => {
  server.use(http.post('*/api/exports', () => HttpResponse.json({ title: 'Conflict' }, { status: 409 })))
  renderSection()

  await userEvent.click(await screen.findByRole('button', { name: 'Экспортировать' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Conflict')
})
