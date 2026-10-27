import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { App } from './App'

const server = setupServer(
  http.get('*/api/items', () => HttpResponse.json({ items: [], page: 1, pageSize: 20, total: 0 })),
  http.get('*/api/reminders', () => HttpResponse.json([])),
  http.get('*/api/tags', () => HttpResponse.json([])),
  http.get('*/api/collections', () => HttpResponse.json([])),
  http.get('*/api/me', () =>
    HttpResponse.json({
      id: '1',
      email: 'a@b.c',
      displayName: 'Alice',
      settings: { timezone: 'UTC', digestFrequency: 'off', digestDay: 'monday', digestChannels: [], email: null },
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

it('renders the layout and navigates between pages', async () => {
  render(<App />)

  expect(await screen.findByRole('heading', { name: 'Записи' })).toBeInTheDocument()
  expect(screen.getByText('Alice')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('link', { name: 'Поиск' }))

  expect(await screen.findByRole('heading', { name: 'Поиск' })).toBeInTheDocument()
})
