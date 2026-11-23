import { downloadUrl, isActive, statusLabels, useExports, useStartExport } from './exportApi'

const formatDate = (value: string) => new Date(value).toLocaleString('en-US', { dateStyle: 'short', timeStyle: 'short' })

export function ExportSection({ pollIntervalMs = 3000 }: { pollIntervalMs?: number }) {
  const { data: exports, error: loadError } = useExports(pollIntervalMs)
  const start = useStartExport()
  const running = exports?.some(isActive) ?? false

  return (
    <section>
      <h2>Export</h2>
      <p>ZIP archive: items in Markdown with metadata, uploaded files and export.json. The archive is kept for 7 days.</p>
      <button type="button" disabled={running || start.isPending} onClick={() => start.mutate()}>
        Export
      </button>
      {running && <span role="status"> Preparing archive…</span>}
      {(start.error || loadError) && <p role="alert">{(start.error ?? loadError)?.message}</p>}
      {exports && exports.length > 0 && (
        <ul>
          {exports.map((e) => (
            <li key={e.id}>
              {formatDate(e.createdAt)} — {statusLabels[e.status]}
              {e.status === 'ready' && e.expiresAt && (
                <>
                  {' '}
                  (until {formatDate(e.expiresAt)}) <a href={downloadUrl(e.id)}>Download</a>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
