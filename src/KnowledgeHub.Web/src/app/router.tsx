import { createBrowserRouter } from 'react-router'
import { LoginPage } from '../features/auth/LoginPage'
import { RequireAuth } from '../features/auth/RequireAuth'
import { CollectionsPage } from '../features/collections/CollectionsPage'
import { ItemPage, NewItemPage } from '../features/items/ItemPage'
import { ItemsPage } from '../features/items/ItemsPage'
import { SearchPage } from '../features/search/SearchPage'
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
          { path: '/', element: <ItemsPage /> },
          { path: '/items/new', element: <NewItemPage /> },
          { path: '/items/:id', element: <ItemPage /> },
          { path: '/collections', element: <CollectionsPage /> },
          { path: '/search', element: <SearchPage /> },
          { path: '/settings', element: <SettingsPage /> },
        ],
      },
    ],
  },
])
