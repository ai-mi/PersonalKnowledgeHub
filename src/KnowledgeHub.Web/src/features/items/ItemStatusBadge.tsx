import { isInProgress, useItemMutations, type Item } from './itemsApi'

const labels: Record<string, string> = { pending: 'Processing', processing: 'Processing', ready: 'Ready', failed: 'Error' }

/** Processing status; for errors — the message and a "Retry" button. */
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
            Retry
          </button>
        </>
      )}
    </span>
  )
}
