/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    https: false,
    proxy: {
      '/api': { target: 'https://localhost:62318', changeOrigin: true, secure: false },
      '/hubs': { target: 'https://localhost:62318', ws: true, changeOrigin: true, secure: false }
    }
  },
  build: { outDir: 'dist', sourcemap: true },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: ['node_modules/', 'src/test/', '*.config.*']
    }
  }
})
