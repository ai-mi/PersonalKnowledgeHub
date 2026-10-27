import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { ItemPage, NewItemPage } from './ItemPage'
import type { Item } from './itemsApi'

const base: Item = {
  id: 'i1',
  type: 'note',
  title: 'Заметка',
  url: null,
  status: 'ready',
  readAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  tags: ['azure'],
  body: 'Текст **жирный**',
  userNote: 'моя',
  codeLanguage: null,
  error: null,
  collections: [{ id: 'c1', name: 'Работа' }],
}

const server = setupServer(
  http.get('*/api/tags', () => HttpResponse.json([])),
  http.get('*/api/collections', () => HttpResponse.json([])),
  http.get('*/api/me', () => HttpResponse.json({ id: '1', displayName: 'A', email: null, settings: { timezone: 'UTC' } })),
  http.get('*/api/items/i1/reminders', () => HttpResponse.json([])),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      { path: '/', element: <h1>Список</h1> },
      { path: '/items/new', element: <NewItemPage /> },
      { path: '/items/:id', element: <ItemPage /> },
    ],
    { initialEntries: [path] },
  )
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return router
}

it('shows a note with markdown, tags and collections', async () => {
  server.use(http.get('*/api/items/i1', () => HttpResponse.json(base)))
  renderAt('/items/i1')

  expect(await screen.findByRole('heading', { name: 'Заметка' })).toBeInTheDocument()
  expect(screen.getByText('жирный').tagName).toBe('STRONG')
  expect(screen.getByRole('link', { name: '#azure' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Работа' })).toBeInTheDocument()
})

it('shows a link source, falls back to url as title and reports a failed status', async () => {
  server.use(
    http.get('*/api/items/i1', () =>
      HttpResponse.json({ ...base, type: 'link', title: null, url: 'https://e.com/a', body: null, userNote: null, tags: [], collections: [], status: 'failed', error: 'timeout' }),
    ),
  )
  renderAt('/items/i1')

  expect(await screen.findByRole('heading', { name: 'https://e.com/a' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'https://e.com/a' })).toHaveAttribute('target', '_blank')
  expect(screen.getByText(/timeout/)).toBeInTheDocument()
})

it('shows an untitled snippet as highlighted code', async () => {
  server.use(
    http.get('*/api/items/i1', () =>
      HttpResponse.json({ ...base, type: 'snippet', title: null, body: 'let a = 1', codeLanguage: 'javascript', userNote: null }),
    ),
  )
  renderAt('/items/i1')

  expect(await screen.findByRole('heading', { name: 'Без названия' })).toBeInTheDocument()
  expect(document.querySelector('code.hljs')).not.toBeNull()
})

it('shows an error when the item is not found', async () => {
  server.use(http.get('*/api/items/i1', () => HttpResponse.json({ title: 'Not Found' }, { status: 404 })))
  renderAt('/items/i1')

  expect(await screen.findByRole('alert')).toHaveTextContent('Not Found')
})

it('toggles the read mark', async () => {
  let item = base
  const bodies: unknown[] = []
  server.use(
    http.get('*/api/items/i1', () => HttpResponse.json(item)),
    http.patch('*/api/items/i1', async ({ request }) => {
      const body = (await request.json()) as { read: boolean }
      bodies.push(body)
      item = { ...item, readAt: body.read ? '2026-01-02T00:00:00Z' : null }
      return HttpResponse.json(item)
    }),
  )
  renderAt('/items/i1')

  await userEvent.click(await screen.findByRole('button', { name: 'Прочитано' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Вернуть в непрочитанное' }))

  await waitFor(() => expect(bodies).toEqual([{ read: true }, { read: false }]))
})

it('edits an item, and cancel leaves edit mode', async () => {
  let item = base
  let put: unknown
  server.use(
    http.get('*/api/items/i1', () => HttpResponse.json(item)),
    http.put('*/api/items/i1', async ({ request }) => {
      put = await request.json()
      item = { ...item, title: 'Новое' }
      return HttpResponse.json(item)
    }),
  )
  const user = userEvent.setup()
  renderAt('/items/i1')

  await user.click(await screen.findByRole('button', { name: 'Редактировать' }))
  expect(screen.getByLabelText('Тип')).toBeDisabled()
  await user.click(screen.getByRole('button', { name: 'Отмена' }))
  expect(screen.queryByLabelText('Заголовок')).not.toBeInTheDocument()

  await user.click(screen.getByRole('button', { name: 'Редактировать' }))
  await user.clear(screen.getByLabelText('Заголовок'))
  await user.type(screen.getByLabelText('Заголовок'), 'Новое')
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))

  expect(await screen.findByRole('heading', { name: 'Новое' })).toBeInTheDocument()
  expect(put).toMatchObject({ title: 'Новое', tags: ['azure'], collectionIds: ['c1'] })
})

it('deletes only after confirmation and returns to the list', async () => {
  let deleted = 0
  server.use(
    http.get('*/api/items/i1', () => HttpResponse.json(base)),
    http.delete('*/api/items/i1', () => {
      deleted++
      return new HttpResponse(null, { status: 204 })
    }),
  )
  vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
  renderAt('/items/i1')

  const button = await screen.findByRole('button', { name: 'Удалить' })
  await userEvent.click(button)
  expect(deleted).toBe(0)

  await userEvent.click(button)
  expect(await screen.findByRole('heading', { name: 'Список' })).toBeInTheDocument()
  expect(deleted).toBe(1)
})

it('creates a new note and opens it', async () => {
  let posted: unknown
  server.use(
    http.post('*/api/items', async ({ request }) => {
      posted = await request.json()
      return HttpResponse.json(base, { status: 201 })
    }),
    http.get('*/api/items/i1', () => HttpResponse.json(base)),
  )
  const user = userEvent.setup()
  renderAt('/items/new')

  await user.type(screen.getByLabelText('Заголовок'), 'Заметка')
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))

  expect(await screen.findByRole('heading', { name: 'Заметка' })).toBeInTheDocument()
  expect(posted).toMatchObject({ type: 'note', title: 'Заметка', body: null, tags: [], collectionIds: [] })
})
