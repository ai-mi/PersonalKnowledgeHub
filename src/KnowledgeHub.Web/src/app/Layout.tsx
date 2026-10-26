import { NavLink, Outlet } from 'react-router'

const links = [
  { to: '/', label: 'Записи' },
  { to: '/search', label: 'Поиск' },
  { to: '/settings', label: 'Настройки' },
]

export function Layout() {
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
      </header>
      <main>
        <Outlet />
      </main>
    </>
  )
}