import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ItemFiltersBar } from '../items/ItemFilters'
import { pageSize } from '../items/itemsApi'
import { SearchResult } from './SearchResult'
import { useSearch, type SearchFilters } from './searchApi'

const debounceMs = 300

export function SearchPage() {
  const [params, setParams] = useSearchParams()
  const q = params.get('q') ?? ''
  const filters: SearchFilters = {
    q,
    type: params.get('type') ?? '',
    tag: params.get('tag') ?? '',
    collection: params.get('collection') ?? '',
    unread: params.get('unread') === 'true',
    page: Math.max(1, Number(params.get('page')) || 1),
  }
  const [input, setInput] = useState(q)
  const { data, isFetching, error } = useSearch(filters)

  // Смена любого параметра возвращает на первую страницу.
  const setParam = (key: string, value: string) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      if (value) next.set(key, value)
      else next.delete(key)
      if (key !== 'page') next.delete('page')
      return next
    })

  // Внешнее изменение q (шапка, «назад») обновляет поле ввода.
  useEffect(() => setInput(q), [q])

  useEffect(() => {
    if (input === q) return
    const timer = setTimeout(() => setParam('q', input), debounceMs)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [input, q])

  const pageCount = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1

  return (
    <>
      <h1>Search</h1>
      <p>
        <input
          type="search"
          aria-label="Query"
          placeholder={'Words, "phrase", -excluded'}
          value={input}
          onChange={(e) => setInput(e.target.value)}
        />
      </p>
      <ItemFiltersBar filters={filters} onChange={setParam} />

      {q.trim() === '' && <p>Enter a query to search items.</p>}
      {isFetching && <p>Searching…</p>}
      {error && <p role="alert">{error.message}</p>}
      {data && q.trim() !== '' && data.items.length === 0 && <p>Nothing found.</p>}
      {q.trim() !== '' && data?.items.map((hit) => <SearchResult key={hit.item.id} hit={hit} />)}

      {data && pageCount > 1 && (
        <p>
          <button type="button" disabled={filters.page <= 1} onClick={() => setParam('page', String(filters.page - 1))}>
            Previous
          </button>{' '}
          Page {filters.page} of {pageCount}{' '}
          <button type="button" disabled={filters.page >= pageCount} onClick={() => setParam('page', String(filters.page + 1))}>
            Next
          </button>
        </p>
      )}
    </>
  )
}
