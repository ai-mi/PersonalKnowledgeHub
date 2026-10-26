import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import { App } from './App'

it('renders the layout and navigates between pages', async () => {
  render(<App />)

  expect(await screen.findByRole('heading', { name: 'Записи' })).toBeInTheDocument()

  await userEvent.click(screen.getByRole('link', { name: 'Поиск' }))

  expect(await screen.findByRole('heading', { name: 'Поиск' })).toBeInTheDocument()
})