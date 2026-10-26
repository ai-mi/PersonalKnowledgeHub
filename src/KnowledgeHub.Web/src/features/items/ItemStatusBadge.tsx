import { isInProgress, useItemMutations, type Item } from './itemsApi'

const labels: Record<string, string> = { pending: 'В обработке', processing: 'В обработке', ready: 'Готово', failed: 'Ошибка' }

/** Статус обработки записи; для ошибки — текст и кнопка «Повторить». */
export function ItemStatusBadge({ item, withRetry = false }: { item: Pick<Item, 'id' | 'status'> & { error?: string | null }; withRetry?: boolean }) {
  const { reprocess } = useItemMutations()
  const label = labels[item.status] ?? item.status

  return (
    <span>
      <small role={isInProgress(item.status) ? 'status' : undefined}>{label}</small>
      {item.status === 'failed' && item.error && <span role="alert"> {item.error}</span>}
      {item.status === 'failed' && withRetry && (
        <>
          {' '}
          <button type="button" disabled={reprocess.isPending} onClick={() => reprocess.mutate(item.id)}>
            Повторить
          </button>
        </>
      )}
    </span>
  )
}
