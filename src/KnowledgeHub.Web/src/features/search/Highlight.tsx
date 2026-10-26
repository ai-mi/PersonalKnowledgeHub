import type { ReactNode } from 'react'

const entities: Record<string, string> = { '&lt;': '<', '&gt;': '>', '&quot;': '"', '&#39;': "'", '&amp;': '&' }

// Бэкенд экранирует HTML фрагмента; в текст возвращаем только эти пять сущностей.
const decode = (text: string) => text.replace(/&(?:lt|gt|quot|#39|amp);/g, (e) => entities[e] ?? e)

/** Рендерит фрагмент поиска: `<mark>` — React-элементы, всё остальное — текст (без innerHTML). */
export function Highlight({ text }: { text: string }) {
  const parts: ReactNode[] = []
  let last = 0
  for (const match of text.matchAll(/<mark>(.*?)<\/mark>/gs)) {
    if (match.index > last) parts.push(decode(text.slice(last, match.index)))
    parts.push(<mark key={match.index}>{decode(match[1] ?? '')}</mark>)
    last = match.index + match[0].length
  }
  if (last < text.length) parts.push(decode(text.slice(last)))
  return <>{parts}</>
}
