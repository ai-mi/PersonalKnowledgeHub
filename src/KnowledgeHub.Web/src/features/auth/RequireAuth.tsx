import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { UNAUTHORIZED_EVENT } from '../../api/client'
import { meQueryKey, useMe } from './useMe'

export function RequireAuth() {
  const queryClient = useQueryClient()
  const location = useLocation()
  const { data: me, isPending, error } = useMe()

  // Сессия истекла посреди работы: любой 401 сбрасывает профиль, и мы уходим на /login.
  useEffect(() => {
    const onUnauthorized = () => queryClient.setQueryData(meQueryKey, null)
    window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized)
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized)
  }, [queryClient])

  if (isPending) {
    return <p>Загрузка…</p>
  }
  if (error) {
    return <p role="alert">Не удалось загрузить профиль: {error.message}</p>
  }
  if (me === null) {
    const returnUrl = location.pathname + location.search
    return <Navigate to={`/login?returnUrl=${encodeURIComponent(returnUrl)}`} replace />
  }
  return <Outlet />
}
