import { useMutation, useQueryClient } from '@tanstack/react-query'
import { NavLink, Outlet, useNavigate, useSearchParams } from 'react-router'
import { apiFetch } from '../api/client'
import { useMe } from '../features/auth/useMe'

const links = [
  { to: '/', label: 'Записи' },
  { to: '/collections', label: 'Коллекции' },
  { to: '/search', label: 'Поиск' },
  { to: '/settings', label: 'Настройки' },
]

export function Layout() {
  const { data: me } = useMe()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const logout = useMutation({
    mutationFn: () => apiFetch('/api/auth/logout', { method: 'POST' }),
    onSuccess: async () => {
      queryClient.clear()
      await navigate('/login', { replace: true })
    },
  })

  return (
    <>
      <header>
        <strong>Knowledge Hub</strong>
        <nav>
          {links.map((link) => (
            <NavLink key={link.to} to={link.to} end={link.to === '/'}>
              {link.label}
            </NavLink>
          ))}
        </nav>
        <form
          role="search"
          onSubmit={(e) => {
            e.preventDefault()
            const q = String(new FormData(e.currentTarget).get('q') ?? '').trim()
            if (q) void navigate(`/search?q=${encodeURIComponent(q)}`)
          }}
        >
          <input
            type="search"
            name="q"
            aria-label="Быстрый поиск"
            placeholder="Поиск"
            key={searchParams.get('q')}
            defaultValue={searchParams.get('q') ?? ''}
          />
        </form>
        <span className="spacer" />
        {me && <span>{me.displayName}</span>}
        <button type="button" onClick={() => logout.mutate()} disabled={logout.isPending}>
          Выйти
        </button>
      </header>
      <main>
        <Outlet />
      </main>
    </>
  )
}
