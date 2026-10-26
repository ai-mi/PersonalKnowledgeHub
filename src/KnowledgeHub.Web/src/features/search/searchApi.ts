import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { apiFetch } from '../../api/client'
import { pageSize, type ItemFilters, type ItemSummary, type Paged } from '../items/itemsApi'

export interface SearchHit {
  item: ItemSummary
  rank: number
  highlights: string[]
}

export type SearchFilters = ItemFilters & { q: string }

export type SearchResponse = Pick<Paged<SearchHit>, 'items' | 'total'>

export function searchUrl({ q, type, tag, collection, unread, page }: SearchFilters): string {
  const params = new URLSearchParams({ q, page: String(page), pageSize: String(pageSize) })
  if (type) params.set('type', type)
  if (tag) params.set('tags', tag)
  if (collection) params.set('collection', collection)
  if (unread) params.set('unread', 'true')
  return `/api/search?${params}`
}

export const useSearch = (filters: SearchFilters) =>
  useQuery({
    queryKey: ['search', filters],
    queryFn: () => apiFetch<SearchResponse>(searchUrl(filters)),
    enabled: filters.q.trim() !== '',
    placeholderData: keepPreviousData,
  })
