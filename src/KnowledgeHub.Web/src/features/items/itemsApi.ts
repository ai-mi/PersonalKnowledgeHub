import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch, apiUpload } from '../../api/client'

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

export const displayTitle = (item: Pick<ItemSummary, 'title' | 'url'>) => item.title ?? item.url ?? 'Без названия'

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

export interface UploadInput {
  file: File
  title: string | null
  userNote: string | null
  tags: string[]
  collectionIds: string[]
}

function toFormData({ file, title, userNote, tags, collectionIds }: UploadInput): FormData {
  const form = new FormData()
  form.append('file', file)
  if (title) form.append('title', title)
  if (userNote) form.append('userNote', userNote)
  tags.forEach((t) => form.append('tags', t))
  collectionIds.forEach((id) => form.append('collectionIds', id))
  return form
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
    refetchInterval: (query) => (query.state.data?.items.some((i) => isInProgress(i.status)) ? POLL_INTERVAL_MS : false),
  })

export const isInProgress = (status: string) => status === 'pending' || status === 'processing'

const POLL_INTERVAL_MS = 2000

/** Пока запись обрабатывается, опрашивает её раз в 2 с; на `ready`/`failed` опрос прекращается. */
export const useItem = (id: string) =>
  useQuery({
    queryKey: ['items', 'one', id],
    queryFn: () => apiFetch<Item>(`/api/items/${id}`),
    refetchInterval: (query) => (query.state.data && isInProgress(query.state.data.status) ? POLL_INTERVAL_MS : false),
  })

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
    reprocess: useMutation({
      mutationFn: (id: string) => apiFetch<Item>(`/api/items/${id}/reprocess`, { method: 'POST' }),
      onSuccess,
    }),
    upload: useMutation({
      mutationFn: ({ input, onProgress }: { input: UploadInput; onProgress?: (fraction: number) => void }) =>
        apiUpload<Item>('/api/items/upload', toFormData(input), onProgress),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (id: string) => apiFetch(`/api/items/${id}`, { method: 'DELETE' }),
      onSuccess,
    }),
  }
}
