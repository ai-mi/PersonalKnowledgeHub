import { Link } from 'react-router'
import { ItemStatusBadge } from './ItemStatusBadge'
import { displayTitle, itemTypeLabels, useItemMutations, type ItemSummary } from './itemsApi'

export function ItemCard({ item }: { item: ItemSummary }) {
  const { setRead } = useItemMutations()
  const isRead = item.readAt !== null

  return (
    <article>
      <small>{itemTypeLabels[item.type]}</small>{' '}
      <Link to={`/items/${item.id}`}>{displayTitle(item)}</Link>{' '}
      {item.tags.map((tag) => (
        <Link key={tag} to={`/?tag=${encodeURIComponent(tag)}`}>
          #{tag}{' '}
        </Link>
      ))}
      {item.type !== 'note' && item.type !== 'snippet' && <ItemStatusBadge item={item} />}{' '}
      <small>{new Date(item.createdAt).toLocaleDateString('ru-RU')}</small>{' '}
      <button type="button" disabled={setRead.isPending} onClick={() => setRead.mutate({ id: item.id, read: !isRead })}>
        {isRead ? 'Вернуть в непрочитанное' : 'Прочитано'}
      </button>
    </article>
  )
}
