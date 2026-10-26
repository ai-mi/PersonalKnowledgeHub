import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { ApiTokensSection } from './ApiTokensSection'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

function renderSection() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ApiTokensSection />
    </QueryClientProvider>,
  )
}

const listed = { id: 't1', name: 'CI', createdAt: '2026-01-01T00:00:00Z', expiresAt: null, revokedAt: null, lastUsedAt: null }

it('shows a new token once and hides it after closing', async () => {
  let tokens: unknown[] = []
  server.use(
    http.get('*/api/me/tokens', () => HttpResponse.json(tokens)),
    http.post('*/api/me/tokens', async ({ request }) => {
      expect(await request.json()).toEqual({ name: 'CI', expiresInDays: null })
      tokens = [listed]
      return HttpResponse.json({ id: 't1', name: 'CI', token: 'kh_secret' })
    }),
  )
  renderSection()

  await userEvent.type(await screen.findByLabelText('Название'), 'CI')
  await userEvent.click(screen.getByRole('button', { name: 'Создать токен' }))

  expect(await screen.findByText('kh_secret')).toBeInTheDocument()
  expect(await screen.findByRole('cell', { name: 'CI' })).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Закрыть' }))

  expect(screen.queryByText('kh_secret')).not.toBeInTheDocument()
})

it('copies the token to the clipboard', async () => {
  server.use(
    http.get('*/api/me/tokens', () => HttpResponse.json([])),
    http.post('*/api/me/tokens', () => HttpResponse.json({ id: 't1', name: 'CI', token: 'kh_secret' })),
  )
  const user = userEvent.setup()
  const writeText = vi.spyOn(navigator.clipboard, 'writeText')
  renderSection()

  await user.type(await screen.findByLabelText('Название'), 'CI')
  await user.click(screen.getByRole('button', { name: 'Создать токен' }))
  await user.click(await screen.findByRole('button', { name: 'Скопировать' }))

  expect(writeText).toHaveBeenCalledWith('kh_secret')
  expect(await screen.findByRole('button', { name: 'Скопировано' })).toBeInTheDocument()
})

it('revokes a token only after confirmation', async () => {
  let revoked = 0
  server.use(
    http.get('*/api/me/tokens', () => HttpResponse.json([listed])),
    http.delete('*/api/me/tokens/t1', () => {
      revoked++
      return new HttpResponse(null, { status: 204 })
    }),
  )
  const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
  renderSection()

  const button = await screen.findByRole('button', { name: 'Отозвать' })
  await userEvent.click(button)
  expect(revoked).toBe(0)

  await userEvent.click(button)
  await waitFor(() => expect(revoked).toBe(1))
  expect(confirm).toHaveBeenCalledTimes(2)
})

it('sends expiresInDays as a number when set', async () => {
  let body: unknown
  server.use(
    http.get('*/api/me/tokens', () => HttpResponse.json([])),
    http.post('*/api/me/tokens', async ({ request }) => {
      body = await request.json()
      return HttpResponse.json({ id: 't1', name: 'CI', token: 'kh_x' })
    }),
  )
  renderSection()

  await userEvent.type(await screen.findByLabelText('Название'), 'CI')
  await userEvent.type(screen.getByLabelText('Срок, дней'), '30')
  await userEvent.click(screen.getByRole('button', { name: 'Создать токен' }))

  await screen.findByText('kh_x')
  expect(body).toEqual({ name: 'CI', expiresInDays: 30 })
})

it('shows the empty state, a load error and a create error', async () => {
  server.use(
    http.get('*/api/me/tokens', () => HttpResponse.json([])),
    http.post('*/api/me/tokens', () => HttpResponse.json({ title: 'Too many' }, { status: 400 })),
  )
  renderSection()
  expect(await screen.findByText('Токенов пока нет.')).toBeInTheDocument()

  await userEvent.type(screen.getByLabelText('Название'), 'CI')
  await userEvent.click(screen.getByRole('button', { name: 'Создать токен' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Too many')
})

it('shows a load error', async () => {
  server.use(http.get('*/api/me/tokens', () => HttpResponse.json({ title: 'Boom' }, { status: 500 })))
  renderSection()

  expect(await screen.findByRole('alert')).toHaveTextContent('Boom')
})

it('shows dates, marks revoked tokens and reports a revoke error', async () => {
  server.use(
    http.get('*/api/me/tokens', () =>
      HttpResponse.json([
        { ...listed, id: 't1', name: 'Old', revokedAt: '2026-02-01T00:00:00Z', expiresAt: '2026-03-01T00:00:00Z', lastUsedAt: '2026-01-15T00:00:00Z' },
        { ...listed, id: 't2', name: 'Live' },
      ]),
    ),
    http.delete('*/api/me/tokens/t2', () => HttpResponse.json({ title: 'Gone' }, { status: 404 })),
  )
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  renderSection()

  expect(await screen.findByText('Отозван')).toBeInTheDocument()
  expect(screen.getAllByRole('button', { name: 'Отозвать' })).toHaveLength(1)
  expect(screen.getByText('01.03.2026')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Отозвать' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Gone')
})
