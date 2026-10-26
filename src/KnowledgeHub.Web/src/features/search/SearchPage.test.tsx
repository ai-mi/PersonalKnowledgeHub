import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { SearchPage } from './SearchPage'

const requested: URL[] = []
const item = {
  id: 'i1',
  type: 'link',
  title: 'Service Bus',
  url: 'https://e.com',
  status: 'ready',
  readAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  tags: [],
}
const defaultHits = [{ item, rank: 1, highlights: ['<mark>очереди</mark> сообщений &lt;b&gt;'] }]
let hits: unknown[] = defaultHits

const server = setupServer(
  http.get('*/api/search', ({ request }) => {
    requested.push(new URL(request.url))
    return HttpResponse.json({ total: hits.length, items: hits })
  }),
  http.get('*/api/tags', () => HttpResponse.json([{ id: 't1', name: 'azure', itemCount: 1 }])),
  http.get('*/api/collections', () => HttpResponse.json([])),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  requested.length = 0
  hits = defaultHits
})
afterAll(() => server.close())

function renderPage(path: string) {
  const router = createMemoryRouter([{ path: '/search', element: <SearchPage /> }], { initialEntries: [path] })
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return router
}

it('shows the empty prompt and sends no request without a query', async () => {
  renderPage('/search')

  expect(await screen.findByText('Введите запрос, чтобы найти записи.')).toBeInTheDocument()
  expect(requested).toHaveLength(0)
})

it('searches by the q from the URL and highlights the fragment', async () => {
  renderPage('/search?q=очередь')

  expect(await screen.findByRole('link', { name: 'Service Bus' })).toBeInTheDocument()
  expect(requested[0]?.searchParams.get('q')).toBe('очередь')
  expect(screen.getByText('очереди').tagName).toBe('MARK')
  expect(screen.getByText(/сообщений <b>/)).toBeInTheDocument()
})

it('debounces typing and puts the query into the URL', async () => {
  const router = renderPage('/search')
  await userEvent.type(screen.getByRole('searchbox', { name: 'Запрос' }), 'azure')

  expect(requested).toHaveLength(0)
  await waitFor(() => expect(router.state.location.search).toBe('?q=azure'))
  await waitFor(() => expect(requested.at(-1)?.searchParams.get('q')).toBe('azure'))
  expect(requested).toHaveLength(1)
})

it('a filter change updates the URL and the request, and resets the page', async () => {
  const user = userEvent.setup()
  const router = renderPage('/search?q=azure&page=2')
  await screen.findByRole('link', { name: 'Service Bus' })
  await screen.findByRole('option', { name: 'azure' })

  await user.selectOptions(screen.getByLabelText('Тип'), 'link')
  await user.selectOptions(screen.getByLabelText('Тег'), 'azure')
  await user.click(screen.getByRole('checkbox', { name: 'Непрочитанное' }))

  await waitFor(() => {
    const last = requested.at(-1)!.searchParams
    expect(last.get('type')).toBe('link')
    expect(last.get('tags')).toBe('azure')
    expect(last.get('unread')).toBe('true')
    expect(last.get('page')).toBe('1')
  })
  expect(router.state.location.search).toContain('type=link')
  expect(router.state.location.search).not.toContain('page=')
})

it('shows "nothing found"', async () => {
  hits = []
  renderPage('/search?q=zzz')

  expect(await screen.findByText('Ничего не найдено.')).toBeInTheDocument()
})

it('shows an ApiError', async () => {
  server.use(http.get('*/api/search', () => HttpResponse.json({ title: 'Boom' }, { status: 500 })))
  renderPage('/search?q=x')

  expect(await screen.findByRole('alert')).toHaveTextContent('Boom')
})
