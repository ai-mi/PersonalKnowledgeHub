import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { ApiError, apiFetch, UNAUTHORIZED_EVENT } from './client'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

it('returns parsed JSON on success', async () => {
  server.use(http.get('*/api/ping', () => HttpResponse.json({ ok: true })))

  await expect(apiFetch<{ ok: boolean }>('/api/ping')).resolves.toEqual({ ok: true })
})

it('sends the body as JSON', async () => {
  server.use(
    http.post('*/api/echo', async ({ request }) => HttpResponse.json(await request.json())),
  )

  await expect(apiFetch('/api/echo', { method: 'POST', body: { a: 1 } })).resolves.toEqual({ a: 1 })
})

it('maps ProblemDetails to ApiError', async () => {
  server.use(
    http.post('*/api/items', () =>
      HttpResponse.json(
        { title: 'Validation', status: 400, errors: { title: ['Too long'] } },
        { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    ),
  )

  const error = await apiFetch('/api/items', { method: 'POST', body: {} }).catch((e: unknown) => e)

  expect(error).toBeInstanceOf(ApiError)
  expect(error).toMatchObject({ status: 400, title: 'Validation', errors: { title: ['Too long'] } })
})

it('dispatches the unauthorized event on 401', async () => {
  server.use(http.get('*/api/me', () => new HttpResponse(null, { status: 401 })))
  const listener = vi.fn()
  window.addEventListener(UNAUTHORIZED_EVENT, listener)

  await expect(apiFetch('/api/me')).rejects.toMatchObject({ status: 401 })

  expect(listener).toHaveBeenCalledOnce()
  window.removeEventListener(UNAUTHORIZED_EVENT, listener)
})

it('returns undefined for 204 No Content', async () => {
  server.use(http.delete('*/api/items/1', () => new HttpResponse(null, { status: 204 })))

  await expect(apiFetch('/api/items/1', { method: 'DELETE' })).resolves.toBeUndefined()
})

it('falls back to a generic ApiError when the error body is not JSON', async () => {
  server.use(http.get('*/api/boom', () => new HttpResponse('<html>Bad gateway</html>', { status: 502 })))

  const error = await apiFetch('/api/boom').catch((e: unknown) => e)

  expect(error).toBeInstanceOf(ApiError)
  expect(error).toMatchObject({ status: 502, title: 'HTTP 502', message: 'HTTP 502' })
})

it('uses ProblemDetails.detail as the error message', async () => {
  server.use(
    http.get('*/api/items/9', () =>
      HttpResponse.json({ title: 'Not Found', detail: 'Item 9 does not exist', status: 404 }, { status: 404 }),
    ),
  )

  await expect(apiFetch('/api/items/9')).rejects.toThrow('Item 9 does not exist')
})

it.each([403, 404, 500])('does not dispatch the unauthorized event on %i', async (status) => {
  server.use(http.get('*/api/x', () => new HttpResponse(null, { status })))
  const listener = vi.fn()
  window.addEventListener(UNAUTHORIZED_EVENT, listener)

  await expect(apiFetch('/api/x')).rejects.toBeInstanceOf(ApiError)

  expect(listener).not.toHaveBeenCalled()
  window.removeEventListener(UNAUTHORIZED_EVENT, listener)
})

it('sends cookies and does not set Content-Type on bodyless requests', async () => {
  server.use(http.get('*/api/me', () => HttpResponse.json({})))
  const fetchSpy = vi.spyOn(globalThis, 'fetch')

  await apiFetch('/api/me')

  const init = fetchSpy.mock.calls[0][1]
  expect(init?.credentials).toBe('include')
  expect(init?.headers).toEqual({ Accept: 'application/json' })
  fetchSpy.mockRestore()
})

it('sends falsy bodies such as false and 0 instead of dropping them', async () => {
  server.use(http.put('*/api/flag', async ({ request }) => HttpResponse.json({ got: await request.json() })))

  await expect(apiFetch('/api/flag', { method: 'PUT', body: false })).resolves.toEqual({ got: false })
})

it('rejects with a non-ApiError and no unauthorized event on network failure', async () => {
  server.use(http.get('*/api/down', () => HttpResponse.error()))
  const listener = vi.fn()
  window.addEventListener(UNAUTHORIZED_EVENT, listener)

  const error = await apiFetch('/api/down').catch((e: unknown) => e)

  expect(error).toBeInstanceOf(Error)
  expect(error).not.toBeInstanceOf(ApiError)
  expect(listener).not.toHaveBeenCalled()
  window.removeEventListener(UNAUTHORIZED_EVENT, listener)
})

it('aborts the request when the signal is aborted', async () => {
  server.use(http.get('*/api/slow', async () => new Promise<Response>(() => {})))
  const controller = new AbortController()

  const pending = apiFetch('/api/slow', { signal: controller.signal })
  controller.abort()

  await expect(pending).rejects.toMatchObject({ name: 'AbortError' })
})

