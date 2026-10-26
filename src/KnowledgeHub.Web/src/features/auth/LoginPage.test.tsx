import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { LoginPage } from './LoginPage'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      { path: '/login', element: <LoginPage /> },
      { path: '/target', element: <h1>Target</h1> },
      { path: '/', element: <h1>Home</h1> },
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

it('links to both providers carrying the local returnUrl', () => {
  renderAt('/login?returnUrl=%2Ftarget%3Fa%3D1')

  expect(screen.getByRole('link', { name: 'Войти через Microsoft' })).toHaveAttribute(
    'href',
    '/api/auth/login/microsoft?returnUrl=%2Ftarget%3Fa%3D1',
  )
  expect(screen.getByRole('link', { name: 'Войти через Google' })).toHaveAttribute(
    'href',
    '/api/auth/login/google?returnUrl=%2Ftarget%3Fa%3D1',
  )
})

it.each(['https://evil.com', '//evil.com', '/\\evil.com', 'evil'])('replaces non-local returnUrl %s with /', (value) => {
  renderAt(`/login?returnUrl=${encodeURIComponent(value)}`)

  expect(screen.getByRole('link', { name: 'Войти через Google' })).toHaveAttribute('href', '/api/auth/login/google?returnUrl=%2F')
})

it('defaults returnUrl to / and hides the error', () => {
  renderAt('/login')

  expect(screen.getByRole('link', { name: 'Войти через Microsoft' })).toHaveAttribute('href', '/api/auth/login/microsoft?returnUrl=%2F')
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('shows a message after a failed external sign-in', () => {
  renderAt('/login?error=1')

  expect(screen.getByRole('alert')).toHaveTextContent('Не удалось войти')
})

it('dev login posts the email and navigates to the returnUrl', async () => {
  let body: unknown
  server.use(
    http.post('*/api/auth/dev-login', async ({ request }) => {
      body = await request.json()
      return new HttpResponse(null, { status: 204 })
    }),
  )
  const router = renderAt('/login?returnUrl=%2Ftarget')

  await userEvent.clear(screen.getByLabelText('E-mail'))
  await userEvent.type(screen.getByLabelText('E-mail'), 'me@x.io')
  await userEvent.click(screen.getByRole('button', { name: 'Войти' }))

  expect(await screen.findByRole('heading', { name: 'Target' })).toBeInTheDocument()
  expect(body).toEqual({ email: 'me@x.io' })
  expect(router.state.location.pathname).toBe('/target')
})

it('dev login shows the server error', async () => {
  server.use(http.post('*/api/auth/dev-login', () => HttpResponse.json({ title: 'Disabled' }, { status: 404 })))
  renderAt('/login')

  await userEvent.click(screen.getByRole('button', { name: 'Войти' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Disabled')
})
