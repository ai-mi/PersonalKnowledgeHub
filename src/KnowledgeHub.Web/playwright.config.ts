import { defineConfig, devices } from '@playwright/test'

const apiUrl = 'http://localhost:5000'
const webUrl = 'http://localhost:5173'

// Smoke-тест идёт через Vite dev-сервер (там доступна форма dev-входа, import.meta.env.DEV) и настоящий API.
// Предусловие: PostgreSQL и Azurite из `docker compose up -d`.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: webUrl,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project ../KnowledgeHub.Api --no-launch-profile --urls http://localhost:5000',
      url: `${apiUrl}/health`,
      env: { ASPNETCORE_ENVIRONMENT: 'Development', Auth__EnableDevLogin: 'true' },
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
    },
    {
      command: 'npm run dev -- --port 5173 --strictPort',
      url: webUrl,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
})
