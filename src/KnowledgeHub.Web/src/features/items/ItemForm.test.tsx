import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import { ItemForm } from './ItemForm'
import type { Item } from './itemsApi'

const server = setupServer(
  http.get('*/api/tags', () => HttpResponse.json([{ id: 't1', name: 'azure', itemCount: 1 }])),
  http.get('*/api/collections', () => HttpResponse.json([{ id: 'c1', name: 'Работа', description: null, itemCount: 0 }])),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderForm(onSubmit: Parameters<typeof ItemForm>[0]['onSubmit']) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ItemForm submitLabel="Сохранить" onSubmit={onSubmit} />
    </QueryClientProvider>,
  )
}

it('shows a field error from ProblemDetails next to the field', async () => {
  const onSubmit = vi.fn().mockRejectedValue(new ApiError(400, { errors: { title: ['Заголовок слишком длинный.'] } }))
  renderForm(onSubmit)

  await userEvent.type(screen.getByLabelText('Заголовок'), 'x')
  await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))

  expect(await screen.findByText('Заголовок слишком длинный.')).toBeInTheDocument()
})

it('submits type-specific fields with tags and collections', async () => {
  const onSubmit = vi.fn().mockResolvedValue(undefined)
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.selectOptions(screen.getByLabelText('Тип'), 'snippet')
  await user.type(screen.getByLabelText('Язык'), 'js')
  await user.type(screen.getByLabelText('Код'), 'let a = 1')
  await user.type(screen.getByLabelText('Теги'), '#azure{Enter}new,')
  await user.click(await screen.findByRole('checkbox', { name: 'Работа' }))
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))

  expect(onSubmit).toHaveBeenCalledWith({
    type: 'snippet',
    title: null,
    url: null,
    body: 'let a = 1',
    userNote: null,
    codeLanguage: 'js',
    tags: ['azure', 'new'],
    collectionIds: ['c1'],
  })
})

it('submits a link with its url and nulls out blank fields', async () => {
  const onSubmit = vi.fn().mockResolvedValue(undefined)
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.selectOptions(screen.getByLabelText('Тип'), 'link')
  await user.type(screen.getByLabelText('Адрес'), 'https://e.com/a')
  await user.type(screen.getByLabelText('Заголовок'), '   ')
  await user.type(screen.getByLabelText('Моя заметка'), '  ')
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))

  expect(onSubmit).toHaveBeenCalledWith({
    type: 'link',
    title: null,
    url: 'https://e.com/a',
    body: null,
    userNote: null,
    codeLanguage: null,
    tags: [],
    collectionIds: [],
  })
})

it('shows non-field errors, with a fallback for non-Error rejections', async () => {
  const onSubmit = vi.fn().mockRejectedValueOnce(new Error('Сеть недоступна')).mockRejectedValueOnce('boom')
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.click(screen.getByRole('button', { name: 'Сохранить' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Сеть недоступна')

  await user.click(screen.getByRole('button', { name: 'Сохранить' }))
  expect(await screen.findByText('Не удалось сохранить')).toBeInTheDocument()
})

it('shows errors for every field and clears them on resubmit', async () => {
  const onSubmit = vi
    .fn()
    .mockRejectedValueOnce(new ApiError(400, { errors: { Body: ['body!'], userNote: ['note!'] } }))
    .mockResolvedValueOnce(undefined)
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.click(screen.getByRole('button', { name: 'Сохранить' }))
  expect(await screen.findByText('body!')).toBeInTheDocument()
  expect(screen.getByText('note!')).toBeInTheDocument()

  await user.click(screen.getByRole('button', { name: 'Сохранить' }))
  await waitFor(() => expect(screen.queryByText('body!')).not.toBeInTheDocument())
})

it('shows url and codeLanguage errors', async () => {
  const onSubmit = vi.fn().mockRejectedValue(new ApiError(400, { errors: { url: ['url!'], codeLanguage: ['lang!'] } }))
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.selectOptions(screen.getByLabelText('Тип'), 'link')
  await user.type(screen.getByLabelText('Адрес'), 'https://e.com')
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))
  expect(await screen.findByText('url!')).toBeInTheDocument()

  await user.selectOptions(screen.getByLabelText('Тип'), 'snippet')
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))
  expect(await screen.findByText('lang!')).toBeInTheDocument()
})

it('edits an existing link: type and url are locked, collections can be unticked', async () => {
  const onSubmit = vi.fn().mockResolvedValue(undefined)
  const item: Item = {
    id: 'i1',
    type: 'link',
    title: 'T',
    url: 'https://e.com',
    status: 'ready',
    readAt: null,
    createdAt: '',
    updatedAt: '',
    tags: ['x'],
    body: null,
    userNote: 'n',
    codeLanguage: null,
    error: null,
    collections: [{ id: 'c1', name: 'Работа' }],
  }
  const user = userEvent.setup()
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ItemForm item={item} submitLabel="Сохранить" onSubmit={onSubmit} />
    </QueryClientProvider>,
  )

  expect(screen.getByLabelText('Тип')).toBeDisabled()
  expect(screen.getByLabelText('Адрес')).toBeDisabled()
  await user.click(await screen.findByRole('checkbox', { name: 'Работа' }))
  await user.click(screen.getByRole('button', { name: 'Сохранить' }))

  expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ title: 'T', userNote: 'n', tags: ['x'], collectionIds: [] }))
})
