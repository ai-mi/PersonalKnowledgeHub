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
  http.get('*/api/collections', () => HttpResponse.json([{ id: 'c1', name: 'Work', description: null, itemCount: 0 }])),
)

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderForm(onSubmit: Parameters<typeof ItemForm>[0]['onSubmit']) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ItemForm submitLabel="Save" onSubmit={onSubmit} />
    </QueryClientProvider>,
  )
}

it('shows a field error from ProblemDetails next to the field', async () => {
  const onSubmit = vi.fn().mockRejectedValue(new ApiError(400, { errors: { title: ['Title is too long.'] } }))
  renderForm(onSubmit)

  await userEvent.type(screen.getByLabelText('Title'), 'x')
  await userEvent.click(screen.getByRole('button', { name: 'Save' }))

  expect(await screen.findByText('Title is too long.')).toBeInTheDocument()
})

it('submits type-specific fields with tags and collections', async () => {
  const onSubmit = vi.fn().mockResolvedValue(undefined)
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.selectOptions(screen.getByLabelText('Type'), 'snippet')
  await user.type(screen.getByLabelText('Language'), 'js')
  await user.type(screen.getByLabelText('Code'), 'let a = 1')
  await user.type(screen.getByLabelText('Tags'), '#azure{Enter}new,')
  await user.click(await screen.findByRole('checkbox', { name: 'Work' }))
  await user.click(screen.getByRole('button', { name: 'Save' }))

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

  await user.selectOptions(screen.getByLabelText('Type'), 'link')
  await user.type(screen.getByLabelText('URL'), 'https://e.com/a')
  await user.type(screen.getByLabelText('Title'), '   ')
  await user.type(screen.getByLabelText('My note'), '  ')
  await user.click(screen.getByRole('button', { name: 'Save' }))

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
  const onSubmit = vi.fn().mockRejectedValueOnce(new Error('Network is unavailable')).mockRejectedValueOnce('boom')
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.click(screen.getByRole('button', { name: 'Save' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Network is unavailable')

  await user.click(screen.getByRole('button', { name: 'Save' }))
  expect(await screen.findByText('Could not save')).toBeInTheDocument()
})

it('shows errors for every field and clears them on resubmit', async () => {
  const onSubmit = vi
    .fn()
    .mockRejectedValueOnce(new ApiError(400, { errors: { Body: ['body!'], userNote: ['note!'] } }))
    .mockResolvedValueOnce(undefined)
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.click(screen.getByRole('button', { name: 'Save' }))
  expect(await screen.findByText('body!')).toBeInTheDocument()
  expect(screen.getByText('note!')).toBeInTheDocument()

  await user.click(screen.getByRole('button', { name: 'Save' }))
  await waitFor(() => expect(screen.queryByText('body!')).not.toBeInTheDocument())
})

it('shows url and codeLanguage errors', async () => {
  const onSubmit = vi.fn().mockRejectedValue(new ApiError(400, { errors: { url: ['url!'], codeLanguage: ['lang!'] } }))
  const user = userEvent.setup()
  renderForm(onSubmit)

  await user.selectOptions(screen.getByLabelText('Type'), 'link')
  await user.type(screen.getByLabelText('URL'), 'https://e.com')
  await user.click(screen.getByRole('button', { name: 'Save' }))
  expect(await screen.findByText('url!')).toBeInTheDocument()

  await user.selectOptions(screen.getByLabelText('Type'), 'snippet')
  await user.click(screen.getByRole('button', { name: 'Save' }))
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
    collections: [{ id: 'c1', name: 'Work' }],
  }
  const user = userEvent.setup()
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ItemForm item={item} submitLabel="Save" onSubmit={onSubmit} />
    </QueryClientProvider>,
  )

  expect(screen.getByLabelText('Type')).toBeDisabled()
  expect(screen.getByLabelText('URL')).toBeDisabled()
  await user.click(await screen.findByRole('checkbox', { name: 'Work' }))
  await user.click(screen.getByRole('button', { name: 'Save' }))

  expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ title: 'T', userNote: 'n', tags: ['x'], collectionIds: [] }))
})
