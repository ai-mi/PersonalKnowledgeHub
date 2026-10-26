import ReactMarkdown from 'react-markdown'
import { CodeBlock } from './CodeBlock'

/** Единственное место рендера Markdown: сырой HTML не интерпретируется (нет rehype-raw), ссылки открываются безопасно. */
export function Markdown({ children }: { children: string }) {
  return (
    <ReactMarkdown
      components={{
        a: ({ node: _node, ...props }) => <a {...props} target="_blank" rel="noopener noreferrer" />,
        code: ({ node: _node, className, children: code, ...props }) => {
          const language = /language-([\w-]+)/.exec(className ?? '')?.[1]
          return language === undefined ? (
            <code className={className} {...props}>
              {code}
            </code>
          ) : (
            <CodeBlock code={String(code).replace(/\n$/, '')} language={language} />
          )
        },
      }}
    >
      {children}
    </ReactMarkdown>
  )
}
