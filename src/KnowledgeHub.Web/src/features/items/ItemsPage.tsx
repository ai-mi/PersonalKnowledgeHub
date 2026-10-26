import { Link, useSearchParams } from 'react-router'
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

  // Смена любого фильтра возвращает на первую страницу.
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
      <h1>Записи</h1>
      <p>
        <Link to="/items/new">Новая запись</Link>
      </p>
      <ItemFiltersBar filters={filters} onChange={setParam} />

      {isPending && <p>Загрузка…</p>}
      {error && <p role="alert">{error.message}</p>}
      {data?.items.length === 0 && <p>Записей не найдено.</p>}
      {data?.items.map((item) => <ItemCard key={item.id} item={item} />)}

      {data && pageCount > 1 && (
        <p>
          <button type="button" disabled={filters.page <= 1} onClick={() => setParam('page', String(filters.page - 1))}>
            Назад
          </button>{' '}
          Страница {filters.page} из {pageCount}{' '}
          <button type="button" disabled={filters.page >= pageCount} onClick={() => setParam('page', String(filters.page + 1))}>
            Вперёд
          </button>
        </p>
      )}
    </>
  )
}
