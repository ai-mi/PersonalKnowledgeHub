import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { useState } from 'react'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { TagInput } from './TagInput'

const server = setupServer(http.get('*/api/tags', () => HttpResponse.json([{ id: 't1', name: 'azure', itemCount: 1 }])))

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function Harness({ initial = [] }: { initial?: string[] }) {
  const [tags, setTags] = useState(initial)
  return (
    <>
      <TagInput value={tags} onChange={setTags} />
      <output>{tags.join('|')}</output>
    </>
  )
}

function renderInput(initial?: string[]) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <Harness initial={initial} />
    </QueryClientProvider>,
  )
}

it('adds tags on Enter and comma, stripping leading #', async () => {
  const user = userEvent.setup()
  renderInput()

  await user.type(screen.getByLabelText('Теги'), '##one{Enter}two,')

  expect(screen.getByRole('status')).toHaveTextContent('one|two')
})

it('ignores empty input and case-insensitive duplicates', async () => {
  const user = userEvent.setup()
  renderInput(['Azure'])

  await user.type(screen.getByLabelText('Теги'), '   {Enter}azure{Enter}#')

  expect(screen.getByRole('status')).toHaveTextContent(/^Azure$/)
})

it('adds a pending tag on blur', async () => {
  const user = userEvent.setup()
  renderInput()

  await user.type(screen.getByLabelText('Теги'), 'later')
  await user.tab()

  expect(screen.getByRole('status')).toHaveTextContent('later')
})

it('removes the last tag on Backspace with an empty draft, but not with text', async () => {
  const user = userEvent.setup()
  renderInput(['a', 'b'])

  const input = screen.getByLabelText('Теги')
  await user.type(input, 'x{Backspace}')
  expect(screen.getByRole('status')).toHaveTextContent('a|b')

  await user.type(input, '{Backspace}')
  expect(screen.getByRole('status')).toHaveTextContent(/^a$/)
})

it('removes a tag by clicking it', async () => {
  const user = userEvent.setup()
  renderInput(['a', 'b'])

  await user.click(screen.getByRole('button', { name: 'Убрать тег a' }))

  expect(screen.getByRole('status')).toHaveTextContent(/^b$/)
})

it('suggests existing tags', async () => {
  renderInput()

  await screen.findByText((_, el) => el?.tagName === 'OPTION' && el.getAttribute('value') === 'azure')
})
