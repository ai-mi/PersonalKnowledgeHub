import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../../api/client'

export type ItemType = 'link' | 'note' | 'snippet' | 'document'

export const itemTypeLabels: Record<ItemType, string> = {
  link: 'Ссылка',
  note: 'Заметка',
  snippet: 'Фрагмент кода',
  document: 'Документ',
}

export interface ItemSummary {
  id: string
  type: ItemType
  title: string | null
  url: string | null
  status: string
  readAt: string | null
  createdAt: string
  updatedAt: string
  tags: string[]
}

export interface CollectionRef {
  id: string
  name: string
}

export interface Item extends ItemSummary {
  body: string | null
  userNote: string | null
  codeLanguage: string | null
  error: string | null
  collections: CollectionRef[]
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface TagInfo {
  id: string
  name: string
  itemCount: number
}

/** Тело `POST /api/items`; для `PUT` лишние `type` и `url` сервер игнорирует. */
export interface ItemInput {
  type: ItemType
  title: string | null
  url: string | null
  body: string | null
  userNote: string | null
  codeLanguage: string | null
  tags: string[]
  collectionIds: string[]
}

export interface ItemFilters {
  type: string
  tag: string
  collection: string
  unread: boolean
  page: number
}

export const pageSize = 20

export function itemsUrl({ type, tag, collection, unread, page }: ItemFilters): string {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  if (type) params.set('type', type)
  if (tag) params.set('tags', tag)
  if (collection) params.set('collection', collection)
  if (unread) params.set('unread', 'true')
  return `/api/items?${params}`
}

export const useItems = (filters: ItemFilters) =>
  useQuery({
    queryKey: ['items', 'list', filters],
    queryFn: () => apiFetch<Paged<ItemSummary>>(itemsUrl(filters)),
    placeholderData: keepPreviousData,
  })

export const useItem = (id: string) =>
  useQuery({ queryKey: ['items', 'one', id], queryFn: () => apiFetch<Item>(`/api/items/${id}`) })

export const useTags = () => useQuery({ queryKey: ['tags'], queryFn: () => apiFetch<TagInfo[]>('/api/tags') })

export function useItemMutations() {
  const queryClient = useQueryClient()
  // Любое изменение записи может поменять списки, счётчики тегов и коллекций.
  const onSuccess = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['items'] }),
      queryClient.invalidateQueries({ queryKey: ['tags'] }),
      queryClient.invalidateQueries({ queryKey: ['collections'] }),
    ])

  return {
    create: useMutation({
      mutationFn: (input: ItemInput) => apiFetch<Item>('/api/items', { method: 'POST', body: input }),
      onSuccess,
    }),
    update: useMutation({
      mutationFn: ({ id, input }: { id: string; input: ItemInput }) =>
        apiFetch<Item>(`/api/items/${id}`, { method: 'PUT', body: input }),
      onSuccess,
    }),
    setRead: useMutation({
      mutationFn: ({ id, read }: { id: string; read: boolean }) =>
        apiFetch<Item>(`/api/items/${id}`, { method: 'PATCH', body: { read } }),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (id: string) => apiFetch(`/api/items/${id}`, { method: 'DELETE' }),
      onSuccess,
    }),
  }
}
