import DOMPurify from 'dompurify'
import hljs from 'highlight.js/lib/common'

interface CodeBlockProps {
  code: string
  language?: string | null
}

/** Подсветка по языку; результат highlight.js всё равно проходит через DOMPurify. Неизвестный язык — простой текст. */
export function CodeBlock({ code, language }: CodeBlockProps) {
  if (language == null || hljs.getLanguage(language) === undefined) {
    return (
      <pre>
        <code>{code}</code>
      </pre>
    )
  }

  const html = DOMPurify.sanitize(hljs.highlight(code, { language, ignoreIllegals: true }).value)
  return (
    <pre>
      <code className={`hljs language-${language}`} dangerouslySetInnerHTML={{ __html: html }} />
    </pre>
  )
}
