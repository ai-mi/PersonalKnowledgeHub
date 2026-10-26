import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { CollectionsPage } from './CollectionsPage'
import type { Collection } from './collectionsApi'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

function renderPage() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <CollectionsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const work: Collection = { id: 'c1', name: 'Работа', description: 'проекты', itemCount: 3 }

it('shows an empty state', async () => {
  server.use(http.get('*/api/collections', () => HttpResponse.json([])))
  renderPage()

  expect(await screen.findByText('Коллекций пока нет.')).toBeInTheDocument()
})

it('shows a load error', async () => {
  server.use(http.get('*/api/collections', () => HttpResponse.json({ title: 'Boom' }, { status: 500 })))
  renderPage()

  expect(await screen.findByRole('alert')).toHaveTextContent('Boom')
})

it('creates a collection and clears the input; a failed create shows the error', async () => {
  let list: Collection[] = []
  let fail = false
  server.use(
    http.get('*/api/collections', () => HttpResponse.json(list)),
    http.post('*/api/collections', async ({ request }) => {
      if (fail) return HttpResponse.json({ title: 'Duplicate' }, { status: 409 })
      const body = (await request.json()) as { name: string }
      list = [{ ...work, name: body.name }]
      return HttpResponse.json(list[0], { status: 201 })
    }),
  )
  const user = userEvent.setup()
  renderPage()

  await user.type(await screen.findByLabelText('Новая коллекция'), 'Работа')
  await user.click(screen.getByRole('button', { name: 'Создать' }))

  expect(await screen.findByRole('link', { name: 'Работа' })).toHaveAttribute('href', '/?collection=c1')
  expect(screen.getByLabelText('Новая коллекция')).toHaveValue('')

  fail = true
  await user.type(screen.getByLabelText('Новая коллекция'), 'Другая')
  await user.click(screen.getByRole('button', { name: 'Создать' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Duplicate')
})

it('renames a collection, and shows a rename error', async () => {
  let list = [work]
  let fail = true
  server.use(
    http.get('*/api/collections', () => HttpResponse.json(list)),
    http.put('*/api/collections/c1', async ({ request }) => {
      if (fail) return HttpResponse.json({ title: 'Bad name' }, { status: 400 })
      const body = (await request.json()) as { name: string; description: string | null }
      list = [{ ...work, ...body }]
      return HttpResponse.json(list[0])
    }),
  )
  const user = userEvent.setup()
  renderPage()

  await user.click(await screen.findByRole('button', { name: 'Переименовать' }))
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Bad name')

  fail = false
  await user.clear(screen.getByLabelText('Название'))
  await user.type(screen.getByLabelText('Название'), 'Дом')
  await user.clear(screen.getByLabelText('Описание'))
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))

  expect(await screen.findByRole('link', { name: 'Дом' })).toBeInTheDocument()
  expect(list[0]?.description).toBeNull()
})

it('cancels renaming', async () => {
  server.use(http.get('*/api/collections', () => HttpResponse.json([{ ...work, description: null }])))
  const user = userEvent.setup()
  renderPage()

  await user.click(await screen.findByRole('button', { name: 'Переименовать' }))
  await user.click(screen.getByRole('button', { name: 'Отмена' }))

  expect(screen.getByRole('link', { name: 'Работа' })).toBeInTheDocument()
})

it('deletes a collection only after confirmation, and shows a delete error', async () => {
  let deleted = 0
  server.use(
    http.get('*/api/collections', () => HttpResponse.json([work])),
    http.delete('*/api/collections/c1', () => {
      deleted++
      return deleted === 1 ? HttpResponse.json({ title: 'Locked' }, { status: 409 }) : new HttpResponse(null, { status: 204 })
    }),
  )
  vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValue(true)
  const user = userEvent.setup()
  renderPage()

  const button = await screen.findByRole('button', { name: 'Удалить' })
  await user.click(button)
  expect(deleted).toBe(0)

  await user.click(button)
  expect(await screen.findByRole('alert')).toHaveTextContent('Locked')
  await user.click(button)
  await waitFor(() => expect(deleted).toBe(2))
})
