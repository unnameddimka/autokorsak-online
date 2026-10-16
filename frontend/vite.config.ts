import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// In development the API runs separately (dotnet run / docker); production serves the build from the API.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { '/api': 'http://localhost:8080' },
  },
  test: {
    environment: 'node',
  },
});
