import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { ItemsPage } from './ItemsPage'

const requested: URL[] = []
const summary = {
  id: 'i1',
  type: 'note',
  title: 'Первая',
  url: null,
  status: 'ready',
  readAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  tags: ['azure'],
}
let items: unknown[] = [summary]
let total = 1

const server = setupServer(
  http.get('*/api/items', ({ request }) => {
    requested.push(new URL(request.url))
    return HttpResponse.json({ items, page: 1, pageSize: 20, total })
  }),
  http.get('*/api/reminders', () => HttpResponse.json([])),
  http.get('*/api/tags', () => HttpResponse.json([{ id: 't1', name: 'azure', itemCount: 1 }])),
  http.get('*/api/collections', () => HttpResponse.json([{ id: 'c1', name: 'Работа', description: null, itemCount: 1 }])),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  requested.length = 0
  items = [summary]
  total = 1
})
afterAll(() => server.close())

function renderPage(path = '/') {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={[path]}>
        <ItemsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const lastParam = (name: string) => requested.at(-1)?.searchParams.get(name)

it('lists items with their tags', async () => {
  renderPage()

  expect(await screen.findByRole('link', { name: 'Первая' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: '#azure' })).toBeInTheDocument()
  expect(requested[0]?.searchParams.has('unread')).toBe(false)
})

it('passes unread=true when the unread filter is on, and drops it when off', async () => {
  renderPage()
  await screen.findByRole('link', { name: 'Первая' })

  await userEvent.click(screen.getByRole('checkbox', { name: 'Непрочитанное' }))
  await waitFor(() => expect(lastParam('unread')).toBe('true'))

  await userEvent.click(screen.getByRole('checkbox', { name: 'Непрочитанное' }))
  await waitFor(() => expect(requested.at(-1)?.searchParams.has('unread')).toBe(false))
})

it('passes type, tag and collection filters', async () => {
  const user = userEvent.setup()
  renderPage()
  await screen.findByRole('link', { name: 'Первая' })
  await screen.findByRole('option', { name: 'azure' })
  await screen.findByRole('option', { name: 'Работа' })

  await user.selectOptions(screen.getByLabelText('Тип'), 'snippet')
  await user.selectOptions(screen.getByLabelText('Тег'), 'azure')
  await user.selectOptions(screen.getByLabelText('Коллекция'), 'c1')

  await waitFor(() => {
    expect(lastParam('type')).toBe('snippet')
    expect(lastParam('tags')).toBe('azure')
    expect(lastParam('collection')).toBe('c1')
  })
})

it('reads filters from the URL', async () => {
  renderPage('/?type=link&tag=azure&unread=true')

  await screen.findByRole('link', { name: 'Первая' })
  expect(lastParam('type')).toBe('link')
  expect(lastParam('tags')).toBe('azure')
  expect(lastParam('unread')).toBe('true')
})

it.each(['abc', '0', '-5'])('falls back to page 1 for page=%s', async (page) => {
  renderPage(`/?page=${page}`)

  await screen.findByRole('link', { name: 'Первая' })
  expect(lastParam('page')).toBe('1')
})

it('shows an empty state and hides paging', async () => {
  items = []
  total = 0
  renderPage()

  expect(await screen.findByText('Записей не найдено.')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Вперёд' })).not.toBeInTheDocument()
})

it('shows a load error', async () => {
  server.use(http.get('*/api/items', () => HttpResponse.json({ title: 'Boom' }, { status: 500 })))
  renderPage()

  expect(await screen.findByRole('alert')).toHaveTextContent('Boom')
})

it('pages forward and back, and a filter change resets to page 1', async () => {
  total = 45
  const user = userEvent.setup()
  renderPage()
  await screen.findByText('Страница 1 из 3')
  expect(screen.getByRole('button', { name: 'Назад' })).toBeDisabled()

  await user.click(screen.getByRole('button', { name: 'Вперёд' }))
  await screen.findByText('Страница 2 из 3')
  await user.click(screen.getByRole('button', { name: 'Вперёд' }))
  await screen.findByText('Страница 3 из 3')
  expect(screen.getByRole('button', { name: 'Вперёд' })).toBeDisabled()

  await user.click(screen.getByRole('button', { name: 'Назад' }))
  await screen.findByText('Страница 2 из 3')

  await user.click(screen.getByRole('checkbox', { name: 'Непрочитанное' }))
  await screen.findByText('Страница 1 из 3')
})

it('marks an item read and back from the card', async () => {
  const bodies: unknown[] = []
  server.use(
    http.patch('*/api/items/i1', async ({ request }) => {
      bodies.push(await request.json())
      return HttpResponse.json({})
    }),
  )
  renderPage()
  await userEvent.click(await screen.findByRole('button', { name: 'Прочитано' }))
  await waitFor(() => expect(bodies).toEqual([{ read: true }]))

  items = [{ ...summary, readAt: '2026-01-02T00:00:00Z' }]
  await userEvent.click(screen.getByRole('checkbox', { name: 'Непрочитанное' }))
  await userEvent.click(screen.getByRole('checkbox', { name: 'Непрочитанное' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Вернуть в непрочитанное' }))
  await waitFor(() => expect(bodies).toEqual([{ read: true }, { read: false }]))
})

it('shows the url for an untitled link and a placeholder when there is neither', async () => {
  items = [
    { ...summary, id: 'a', type: 'link', title: null, url: 'https://e.com', tags: [] },
    { ...summary, id: 'b', title: null, tags: [] },
  ]
  renderPage()

  expect(await screen.findByRole('link', { name: 'https://e.com' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Без названия' })).toBeInTheDocument()
})
