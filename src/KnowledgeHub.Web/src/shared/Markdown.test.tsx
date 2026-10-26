import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Markdown } from './Markdown'

it('does not put <script> or onerror handlers into the DOM', () => {
  const { container } = render(
    <Markdown>{'<script>window.hacked = 1</script>\n\n<img src=x onerror="window.hacked = 1">\n\nbody'}</Markdown>,
  )

  expect(container.querySelector('script')).toBeNull()
  expect(container.querySelector('img')).toBeNull()
  expect(container.querySelector('[onerror]')).toBeNull()
  expect(screen.getByText('body')).toBeInTheDocument()
})

it('does not render javascript: links as active', () => {
  const { container } = render(<Markdown>{'[click](javascript:alert(1))'}</Markdown>)

  expect(container.querySelector('a[href^="javascript:"]')).toBeNull()
})

it('opens links in a new tab safely', () => {
  render(<Markdown>{'[docs](https://example.com)'}</Markdown>)

  const link = screen.getByRole('link', { name: 'docs' })
  expect(link).toHaveAttribute('target', '_blank')
  expect(link).toHaveAttribute('rel', 'noopener noreferrer')
})

it('highlights fenced code and keeps it as text', () => {
  const { container } = render(<Markdown>{'```js\nconst a = "<b>x</b>"\n```'}</Markdown>)

  expect(container.querySelector('code.hljs')).not.toBeNull()
  expect(container.querySelector('b')).toBeNull()
  expect(container.textContent).toContain('<b>x</b>')
})

it('renders inline code as plain code without highlighting', () => {
  const { container } = render(<Markdown>{'use `npm test` here'}</Markdown>)

  expect(container.querySelector('code.hljs')).toBeNull()
  expect(container.querySelector('code')).toHaveTextContent('npm test')
})
