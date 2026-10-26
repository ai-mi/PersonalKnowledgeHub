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
