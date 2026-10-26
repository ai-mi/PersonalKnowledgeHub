import { Link, useSearchParams } from 'react-router'
import { useCollections } from '../collections/collectionsApi'
import { ItemCard } from './ItemCard'
import { itemTypeLabels, pageSize, useItems, useTags, type ItemFilters } from './itemsApi'

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
  const { data: tags } = useTags()
  const { data: collections } = useCollections()

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
      <p>
        <label>
          Тип{' '}
          <select value={filters.type} onChange={(e) => setParam('type', e.target.value)}>
            <option value="">Все</option>
            {Object.entries(itemTypeLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>{' '}
        <label>
          Тег{' '}
          <select value={filters.tag} onChange={(e) => setParam('tag', e.target.value)}>
            <option value="">Все</option>
            {tags?.map((t) => (
              <option key={t.id} value={t.name}>
                {t.name}
              </option>
            ))}
          </select>
        </label>{' '}
        <label>
          Коллекция{' '}
          <select value={filters.collection} onChange={(e) => setParam('collection', e.target.value)}>
            <option value="">Все</option>
            {collections?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>{' '}
        <label>
          <input type="checkbox" checked={filters.unread} onChange={(e) => setParam('unread', e.target.checked ? 'true' : '')} />{' '}
          Непрочитанное
        </label>
      </p>

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
