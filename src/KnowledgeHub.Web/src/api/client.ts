export const UNAUTHORIZED_EVENT = 'kh:unauthorized'

const XSRF_COOKIE = 'XSRF-TOKEN'
const XSRF_HEADER = 'X-XSRF-TOKEN'
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS'])

function readCookie(name: string): string | undefined {
  const prefix = `${name}=`
  const entry = document.cookie.split('; ').find((c) => c.startsWith(prefix))
  return entry === undefined ? undefined : decodeURIComponent(entry.slice(prefix.length))
}

interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail?: string
  readonly errors?: Record<string, string[]>
  /** Через сколько повторять запрос — из заголовка `Retry-After` (429 от лимитера API). */
  readonly retryAfterMs?: number

  constructor(status: number, problem: ProblemDetails, retryAfterMs?: number) {
    const title = problem.title ?? `HTTP ${status}`
    super(problem.detail ?? title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = problem.detail
    this.errors = problem.errors
    this.retryAfterMs = retryAfterMs
  }
}

// API отдаёт Retry-After в секундах (RateLimitingExtensions.OnRejected).
function parseRetryAfter(value: string | null): number | undefined {
  const seconds = value === null ? Number.NaN : Number(value)
  return Number.isFinite(seconds) && seconds >= 0 ? seconds * 1000 : undefined
}

export interface RequestOptions {
  method?: string
  body?: unknown
  signal?: AbortSignal
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return {}
  }
}

function xsrfHeaders(method: string): Record<string, string> {
  const xsrf = SAFE_METHODS.has(method.toUpperCase()) ? undefined : readCookie(XSRF_COOKIE)
  return xsrf === undefined ? {} : { [XSRF_HEADER]: xsrf }
}

function toApiError(status: number, problem: ProblemDetails, retryAfter: string | null = null): ApiError {
  if (status === 401) {
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT))
  }
  return new ApiError(status, problem, parseRetryAfter(retryAfter))
}

/** Отправка `multipart/form-data` через XHR — `fetch` не умеет сообщать прогресс отправки. */
export function apiUpload<T>(path: string, form: FormData, onProgress?: (fraction: number) => void): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', new URL(path, window.location.origin))
    xhr.withCredentials = true
    xhr.setRequestHeader('Accept', 'application/json')
    for (const [name, value] of Object.entries(xsrfHeaders('POST'))) {
      xhr.setRequestHeader(name, value)
    }
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress?.(e.loaded / e.total)
    }
    xhr.onerror = () => reject(new ApiError(0, { title: 'Не удалось отправить файл' }))
    xhr.onload = () => {
      let parsed: unknown = {}
      try {
        parsed = JSON.parse(xhr.responseText)
      } catch {
        // Пустое или не JSON-тело: ошибка опишется статусом.
      }
      if (xhr.status >= 200 && xhr.status < 300) resolve(parsed as T)
      else reject(toApiError(xhr.status, parsed as ProblemDetails, xhr.getResponseHeader('Retry-After')))
    }
    xhr.send(form)
  })
}

export async function apiFetch<T>(path: string, { method = 'GET', body, signal }: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', ...xsrfHeaders(method) }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  const response = await fetch(new URL(path, window.location.origin), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'include',
    signal,
  })

  if (!response.ok) {
    throw toApiError(response.status, await readProblem(response), response.headers.get('Retry-After'))
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}