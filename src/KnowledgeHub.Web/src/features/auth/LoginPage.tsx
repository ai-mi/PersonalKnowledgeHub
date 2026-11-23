import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type SubmitEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { apiFetch } from '../../api/client'

/** Принимаем только локальные пути, как и сервер: `//host` и `/\host` — внешние. */
function localReturnUrl(value: string | null): string {
  return value !== null && /^\/(?![/\\])/.test(value) ? value : '/'
}

export function LoginPage() {
  const [params] = useSearchParams()
  const returnUrl = localReturnUrl(params.get('returnUrl'))
  const query = `?returnUrl=${encodeURIComponent(returnUrl)}`

  return (
    <main>
      <h1>Sign in</h1>
      {params.get('error') && <p role="alert">Could not sign in. Please try again.</p>}
      <p>
        <a href={`/api/auth/login/microsoft${query}`}>Sign in with Microsoft</a>
      </p>
      <p>
        <a href={`/api/auth/login/google${query}`}>Sign in with Google</a>
      </p>
      {import.meta.env.DEV && <DevLoginForm returnUrl={returnUrl} />}
    </main>
  )
}

function DevLoginForm({ returnUrl }: { returnUrl: string }) {
  const [email, setEmail] = useState('dev@example.com')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const login = useMutation({
    mutationFn: () => apiFetch('/api/auth/dev-login', { method: 'POST', body: { email } }),
    onSuccess: async () => {
      await queryClient.invalidateQueries()
      await navigate(returnUrl, { replace: true })
    },
  })

  return (
    <form
      onSubmit={(e: SubmitEvent) => {
        e.preventDefault()
        login.mutate()
      }}
    >
      <h2>Dev sign-in</h2>
      <label>
        E-mail <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
      </label>{' '}
      <button type="submit" disabled={login.isPending}>
        Sign in
      </button>
      {login.error && <p role="alert">{login.error.message}</p>}
    </form>
  )
}
