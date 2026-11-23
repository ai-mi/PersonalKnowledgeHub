import { Link, useSearchParams } from 'react-router'
import { UpcomingReminders } from '../reminders/UpcomingReminders'
import { ItemCard } from './ItemCard'
import { ItemFiltersBar } from './ItemFilters'
import { pageSize, useItems, type ItemFilters } from './itemsApi'

export function ItemsPage() {
  const [params, setParams] = useSearchParams()
  const filters: ItemFilters = {
    type: params.get('type') ?? '',
    tag: params.get('tag') ?? '',
    collection: params.get('collection') ?? '',
    unread: params.get('unread') === 'true',
    page: Math.max(1, Number(params.get('page')) || 1),
  }
  const { data, isPending, error } = useItems(filters)

  // Changing any filter resets paging to the first page.
  const setParam = (key: string, value: string) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      if (value) next.set(key, value)
      else next.delete(key)
      if (key !== 'page') next.delete('page')
      return next
    })

  const pageCount = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1

  return (
    <>
      <h1>Items</h1>
      <p>
        <Link to="/items/new">New item</Link> · <Link to="/items/upload">Upload document</Link>
      </p>
      <UpcomingReminders />
      <ItemFiltersBar filters={filters} onChange={setParam} />

      {isPending && <p>Loading…</p>}
      {error && <p role="alert">{error.message}</p>}
      {data?.items.length === 0 && <p>No items found.</p>}
      {data?.items.map((item) => <ItemCard key={item.id} item={item} />)}

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
