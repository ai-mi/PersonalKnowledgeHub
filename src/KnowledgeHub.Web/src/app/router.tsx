import { createBrowserRouter } from 'react-router'
import { Layout } from './Layout'

export const router = createBrowserRouter([
  {
    element: <Layout />,
    children: [
      { path: '/', element: <h1>Записи</h1> },
      { path: '/search', element: <h1>Поиск</h1> },
      { path: '/settings', element: <h1>Настройки</h1> },
    ],
  },
])