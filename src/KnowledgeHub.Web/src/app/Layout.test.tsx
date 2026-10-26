import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { Layout } from './Layout'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderLayout(client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  const router = createMemoryRouter(
    [
      { path: '/login', element: <h1>Login</h1> },
      { element: <Layout />, children: [{ path: '/', element: <h1>Home</h1> }] },
    ],
    { initialEntries: ['/'] },
  )
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return { router, client }
}

it('shows the user name and the navigation', async () => {
  server.use(http.get('*/api/me', () => HttpResponse.json({ id: '1', displayName: 'Alice', email: null, settings: {} })))
  renderLayout()

  expect(await screen.findByText('Alice')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Коллекции' })).toHaveAttribute('href', '/collections')
})

it('logs out: posts, clears the cache and goes to /login', async () => {
  let logouts = 0
  server.use(
    http.get('*/api/me', () => HttpResponse.json({ id: '1', displayName: 'Alice', email: null, settings: {} })),
    http.post('*/api/auth/logout', () => {
      logouts++
      return new HttpResponse(null, { status: 204 })
    }),
  )
  const { router, client } = renderLayout()
  const clear = vi.spyOn(client, 'clear')
  await screen.findByText('Alice')

  await userEvent.click(screen.getByRole('button', { name: 'Выйти' }))

  expect(await screen.findByRole('heading', { name: 'Login' })).toBeInTheDocument()
  expect(logouts).toBe(1)
  expect(router.state.location.pathname).toBe('/login')
  expect(clear).toHaveBeenCalledOnce()
})

it('stays put when logout fails', async () => {
  server.use(
    http.get('*/api/me', () => HttpResponse.json({ id: '1', displayName: 'Alice', email: null, settings: {} })),
    http.post('*/api/auth/logout', () => HttpResponse.json({ title: 'Boom' }, { status: 500 })),
  )
  const { router } = renderLayout()
  await screen.findByText('Alice')

  await userEvent.click(screen.getByRole('button', { name: 'Выйти' }))

  await screen.findByRole('button', { name: 'Выйти' })
  expect(router.state.location.pathname).toBe('/')
})
