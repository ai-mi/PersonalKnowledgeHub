import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './client'

const MAX_BACKOFF_MS = 30_000

/**
 * Повтор после 429 ждёт столько, сколько сказал лимитер API (`Retry-After`): стандартная пауза react-query
 * (1, 2, 4 с) тратила бы попытки внутри того же минутного окна. Остальные ошибки — экспоненциально, как по умолчанию.
 */
function retryDelay(failureCount: number, error: Error): number {
  if (error instanceof ApiError && error.status === 429 && error.retryAfterMs !== undefined) {
    return error.retryAfterMs
  }
  return Math.min(1000 * 2 ** failureCount, MAX_BACKOFF_MS)
}

export function createQueryClient(): QueryClient {
  return new QueryClient({ defaultOptions: { queries: { retryDelay } } })
}
