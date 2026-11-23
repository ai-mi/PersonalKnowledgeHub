import { expect, it } from 'vitest'
import { ApiError } from './client'
import { createQueryClient } from './queryClient'

function retryDelay(failureCount: number, error: Error): number {
  const delay = createQueryClient().getDefaultOptions().queries?.retryDelay
  if (typeof delay !== 'function') throw new Error('retryDelay must be a function')
  return delay(failureCount, error)
}

it('waits for Retry-After before retrying a rate-limited request', () => {
  const error = new ApiError(429, { title: 'Too Many Requests' }, 17_000)

  expect(retryDelay(1, error)).toBe(17_000)
})

it('backs off exponentially for other errors', () => {
  const error = new ApiError(503, { title: 'Service Unavailable' })

  expect(retryDelay(0, error)).toBe(1000)
  expect(retryDelay(1, error)).toBe(2000)
  expect(retryDelay(10, error)).toBe(30_000)
})
