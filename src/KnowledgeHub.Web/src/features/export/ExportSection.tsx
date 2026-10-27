import { downloadUrl, isActive, statusLabels, useExports, useStartExport } from './exportApi'

const formatDate = (value: string) => new Date(value).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short' })

export function ExportSection({ pollIntervalMs = 3000 }: { pollIntervalMs?: number }) {
  const { data: exports, error: loadError } = useExports(pollIntervalMs)
  const start = useStartExport()
  const running = exports?.some(isActive) ?? false

  return (
    <section>
      <h2>Экспорт данных</h2>
      <p>ZIP-архив: записи в Markdown с метаданными, загруженные файлы и export.json. Архив хранится 7 дней.</p>
      <button type="button" disabled={running || start.isPending} onClick={() => start.mutate()}>
        Экспортировать
      </button>
      {running && <span role="status"> Архив собирается…</span>}
      {(start.error || loadError) && <p role="alert">{(start.error ?? loadError)?.message}</p>}
      {exports && exports.length > 0 && (
        <ul>
          {exports.map((e) => (
            <li key={e.id}>
              {formatDate(e.createdAt)} — {statusLabels[e.status]}
              {e.status === 'ready' && e.expiresAt && (
                <>
                  {' '}
                  (до {formatDate(e.expiresAt)}) <a href={downloadUrl(e.id)}>Скачать</a>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
