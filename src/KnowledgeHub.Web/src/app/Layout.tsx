import { useMutation, useQueryClient } from '@tanstack/react-query'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { apiFetch } from '../api/client'
import { useMe } from '../features/auth/useMe'

const links = [
  { to: '/', label: 'Записи' },
  { to: '/search', label: 'Поиск' },
  { to: '/settings', label: 'Настройки' },
]

export function Layout() {
  const { data: me } = useMe()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
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
