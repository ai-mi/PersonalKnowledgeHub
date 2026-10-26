import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { UploadForm } from './UploadForm'

const server = setupServer(
  http.get('*/api/tags', () => HttpResponse.json([])),
  http.get('*/api/collections', () => HttpResponse.json([{ id: 'c1', name: 'Работа', description: null, itemCount: 0 }])),
)
beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const renderForm = (onUploaded = vi.fn()) => {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <UploadForm onUploaded={onUploaded} />
    </QueryClientProvider>,
  )
  return onUploaded
}

const fileInput = () => screen.getByLabelText(/Файл/) as HTMLInputElement
// `accept` в userEvent.upload отсекает файлы по расширению, поэтому проверку формата ведём с applyAccept: false.
const user = () => userEvent.setup({ applyAccept: false })

it('rejects a 25 MB file without sending a request', async () => {
  let uploads = 0
  server.use(http.post('*/api/items/upload', () => (uploads++, HttpResponse.json({}))))
  renderForm()

  const big = new File(['x'], 'big.pdf', { type: 'application/pdf' })
  Object.defineProperty(big, 'size', { value: 25 * 1024 * 1024 })
  await user().upload(fileInput(), big)

  expect(screen.getByRole('alert')).toHaveTextContent('больше 20 МБ')
  await user().click(screen.getByRole('button', { name: 'Загрузить' }))
  expect(uploads).toBe(0)
})

it('rejects an unsupported extension', async () => {
  renderForm()
  await user().upload(fileInput(), new File(['x'], 'run.exe'))
  expect(screen.getByRole('alert')).toHaveTextContent('Допустимые форматы')
})

it('uploads the file with title, tags and collections as multipart', async () => {
  let raw = ''
  server.use(
    http.post('*/api/items/upload', async ({ request }) => {
      raw = await request.text()
      return HttpResponse.json({ id: 'd1' }, { status: 201 })
    }),
  )
  const onUploaded = renderForm()
  const u = user()

  await u.upload(fileInput(), new File(['%PDF-1'], 'report.pdf', { type: 'application/pdf' }))
  await u.type(screen.getByLabelText('Заголовок'), 'Отчёт')
  await u.type(screen.getByLabelText('Теги'), 'azure{Enter}')
  await u.click(await screen.findByRole('checkbox', { name: 'Работа' }))
  await u.click(screen.getByRole('button', { name: 'Загрузить' }))

  await waitFor(() => expect(onUploaded).toHaveBeenCalledWith({ id: 'd1' }))
  // undici не разбирает multipart от jsdom-XHR, поэтому проверяем сырое тело.
  expect(raw).toContain('name="file"; filename="report.pdf"')
  expect(raw).toMatch(/name="title"\r\n\r\nОтчёт/)
  expect(raw).toMatch(/name="tags"\r\n\r\nazure/)
  expect(raw).toMatch(/name="collectionIds"\r\n\r\nc1/)
})
