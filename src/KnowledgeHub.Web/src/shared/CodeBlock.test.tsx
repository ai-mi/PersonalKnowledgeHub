import { render } from '@testing-library/react'
import { expect, it } from 'vitest'
import { CodeBlock } from './CodeBlock'

it('renders plain text when the language is missing', () => {
  const { container } = render(<CodeBlock code="<b>x</b>" />)

  expect(container.querySelector('code.hljs')).toBeNull()
  expect(container.querySelector('b')).toBeNull()
  expect(container.textContent).toBe('<b>x</b>')
})

it('renders plain text for an unknown language', () => {
  const { container } = render(<CodeBlock code="abc" language="nope-lang" />)

  expect(container.querySelector('code.hljs')).toBeNull()
  expect(container.textContent).toBe('abc')
})

it('highlights a known language without turning code into markup', () => {
  const { container } = render(<CodeBlock code={'const s = "<img src=x onerror=1>"'} language="javascript" />)

  expect(container.querySelector('code.hljs.language-javascript')).not.toBeNull()
  expect(container.querySelector('img')).toBeNull()
  expect(container.textContent).toContain('<img src=x onerror=1>')
})
