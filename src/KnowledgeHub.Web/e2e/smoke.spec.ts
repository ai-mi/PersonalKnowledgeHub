import { expect, test } from '@playwright/test'

test('вход → ссылка с заметкой → «Готово» → поиск → прочитано', async ({ page }) => {
  // Уникальный пользователь на запуск: повторное сохранение той же ссылки вернуло бы уже прочитанную запись.
  const email = `e2e-${Date.now()}@example.com`

  await page.goto('/login')
  await page.getByLabel('E-mail').fill(email)
  await page.getByRole('button', { name: 'Войти' }).click()
  await expect(page.getByRole('heading', { name: 'Записи' })).toBeVisible()

  await page.getByRole('link', { name: 'Новая запись' }).click()
  await page.getByLabel('Тип').selectOption('link')
  await page.getByLabel('Адрес').fill('https://example.com')
  await page.getByLabel('Моя заметка').fill('проверка поиска')
  await page.getByRole('button', { name: 'Сохранить' }).click()

  // Воркер скачивает страницу и обрабатывает запись в фоне; страница опрашивает статус.
  await expect(page.getByText('Готово', { exact: true })).toBeVisible({ timeout: 60_000 })

  await page.getByRole('searchbox', { name: 'Быстрый поиск' }).fill('проверка')
  await page.getByRole('searchbox', { name: 'Быстрый поиск' }).press('Enter')
  await expect(page.getByRole('heading', { name: 'Поиск' })).toBeVisible()
  await page.getByRole('link', { name: /example/i }).first().click()

  await page.getByRole('button', { name: 'Прочитано', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Вернуть в непрочитанное' })).toBeVisible()
})
