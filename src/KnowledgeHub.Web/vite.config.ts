import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const backend = 'http://localhost:5000'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../KnowledgeHub.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': backend,
      '/signin-': backend,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
    // e2e-сценарии запускает Playwright (npm run test:e2e), не Vitest.
    exclude: ['e2e/**', 'node_modules/**'],
    coverage: { include: ['src/**'], exclude: ['src/**/*.test.*', 'src/main.tsx'] },
  },
})