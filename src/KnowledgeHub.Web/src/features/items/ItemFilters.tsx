import { useCollections } from '../collections/collectionsApi'
import { itemTypeLabels, useTags, type ItemFilters } from './itemsApi'

type FilterKey = 'type' | 'tag' | 'collection' | 'unread'

interface Props {
  filters: Pick<ItemFilters, FilterKey>
  onChange: (key: FilterKey, value: string) => void
}

/** Общие фильтры списка записей и поиска. */
export function ItemFiltersBar({ filters, onChange }: Props) {
  const { data: tags } = useTags()
  const { data: collections } = useCollections()

  return (
    <p>
      <label>
        Тип{' '}
        <select value={filters.type} onChange={(e) => onChange('type', e.target.value)}>
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
        <select value={filters.tag} onChange={(e) => onChange('tag', e.target.value)}>
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
        <select value={filters.collection} onChange={(e) => onChange('collection', e.target.value)}>
          <option value="">Все</option>
          {collections?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
      </label>{' '}
      <label>
        <input type="checkbox" checked={filters.unread} onChange={(e) => onChange('unread', e.target.checked ? 'true' : '')} />{' '}
        Непрочитанное
      </label>
    </p>
  )
}
