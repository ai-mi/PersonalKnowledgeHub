import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Highlight } from './Highlight'

it('renders <mark> as an element and the rest as text', () => {
  const { container } = render(<Highlight text="очередь <mark>сообщений</mark> в Azure" />)

  expect(screen.getByText('сообщений').tagName).toBe('MARK')
  expect(container).toHaveTextContent('очередь сообщений в Azure')
})

it('keeps escaped HTML as literal text', () => {
  const { container } = render(<Highlight text="&lt;img src=x onerror=alert(1)&gt; <mark>a &amp; b</mark>" />)

  expect(container.querySelector('img')).toBeNull()
  expect(container).toHaveTextContent('<img src=x onerror=alert(1)> a & b')
})

it('does not turn unescaped tags other than <mark> into elements', () => {
  const { container } = render(<Highlight text="<img src=x onerror=alert(1)>" />)

  expect(container.querySelector('img')).toBeNull()
  expect(container).toHaveTextContent('<img src=x onerror=alert(1)>')
})
