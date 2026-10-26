import { createBrowserRouter } from 'react-router'
import { LoginPage } from '../features/auth/LoginPage'
import { RequireAuth } from '../features/auth/RequireAuth'
import { SettingsPage } from '../features/settings/SettingsPage'
import { Layout } from './Layout'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <Layout />,
        children: [
          { path: '/', element: <h1>Записи</h1> },
          { path: '/search', element: <h1>Поиск</h1> },
          { path: '/settings', element: <SettingsPage /> },
        ],
      },
    ],
  },
])
