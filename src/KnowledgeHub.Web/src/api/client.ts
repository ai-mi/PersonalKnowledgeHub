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

  constructor(status: number, problem: ProblemDetails) {
    const title = problem.title ?? `HTTP ${status}`
    super(problem.detail ?? title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = problem.detail
    this.errors = problem.errors
  }
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

export async function apiFetch<T>(path: string, { method = 'GET', body, signal }: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (!SAFE_METHODS.has(method.toUpperCase())) {
    const xsrf = readCookie(XSRF_COOKIE)
    if (xsrf !== undefined) {
      headers[XSRF_HEADER] = xsrf
    }
  }

  const response = await fetch(new URL(path, window.location.origin), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'include',
    signal,
  })

  if (!response.ok) {
    if (response.status === 401) {
      window.dispatchEvent(new Event(UNAUTHORIZED_EVENT))
    }
    throw new ApiError(response.status, await readProblem(response))
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}