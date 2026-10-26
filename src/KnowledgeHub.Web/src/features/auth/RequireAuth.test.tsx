import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { UNAUTHORIZED_EVENT } from '../../api/client'
import { RequireAuth } from './RequireAuth'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      { path: '/login', element: <h1>Login</h1> },
      { element: <RequireAuth />, children: [{ path: '/private', element: <h1>Private</h1> }] },
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

it('redirects to /login with returnUrl on 401', async () => {
  server.use(http.get('*/api/me', () => new HttpResponse(null, { status: 401 })))

  const router = renderAt('/private')

  expect(await screen.findByRole('heading', { name: 'Login' })).toBeInTheDocument()
  expect(router.state.location.pathname).toBe('/login')
  expect(router.state.location.search).toBe('?returnUrl=%2Fprivate')
})

it('renders the protected page when signed in', async () => {
  server.use(http.get('*/api/me', () => HttpResponse.json({ id: '1', displayName: 'A', email: null, settings: {} })))

  renderAt('/private')

  expect(await screen.findByRole('heading', { name: 'Private' })).toBeInTheDocument()
})

it('shows an error instead of redirecting on a server failure', async () => {
  server.use(http.get('*/api/me', () => new HttpResponse(null, { status: 500 })))

  renderAt('/private')

  expect(await screen.findByRole('alert')).toBeInTheDocument()
})

it('redirects to /login when a request returns 401 while signed in', async () => {
  server.use(http.get('*/api/me', () => HttpResponse.json({ id: '1', displayName: 'A', email: null, settings: {} })))
  const router = renderAt('/private?x=1')
  await screen.findByRole('heading', { name: 'Private' })

  window.dispatchEvent(new Event(UNAUTHORIZED_EVENT))

  expect(await screen.findByRole('heading', { name: 'Login' })).toBeInTheDocument()
  expect(router.state.location.search).toBe('?returnUrl=%2Fprivate%3Fx%3D1')
})
