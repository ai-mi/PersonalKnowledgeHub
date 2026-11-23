import { expect, test } from '@playwright/test'

test('sign in → link with note → "Ready" → search → marked read', async ({ page }) => {
  // Unique user per run: re-saving the same link would return an already read item.
  const email = `e2e-${Date.now()}@example.com`

  await page.goto('/login')
  await page.getByLabel('E-mail').fill(email)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: 'Items' })).toBeVisible()

  await page.getByRole('link', { name: 'New item' }).click()
  await page.getByLabel('Type').selectOption('link')
  await page.getByLabel('URL').fill('https://example.com')
  await page.getByLabel('My note').fill('search check')
  await page.getByRole('button', { name: 'Save' }).click()

  // Background worker downloads the page and processes the item; the page polls for the status.
  await expect(page.getByText('Ready', { exact: true })).toBeVisible({ timeout: 60_000 })

  await page.getByRole('searchbox', { name: 'Quick search' }).fill('search')
  await page.getByRole('searchbox', { name: 'Quick search' }).press('Enter')
  await expect(page.getByRole('heading', { name: 'Search' })).toBeVisible()
  await page.getByRole('link', { name: /example/i }).first().click()

  await page.getByRole('button', { name: 'Mark as read', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Mark as unread' })).toBeVisible()
})
