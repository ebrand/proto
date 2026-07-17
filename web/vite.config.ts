import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Pin the dev port so the origin matches the API's CORS allow-list
  // (http://localhost:5173) and the Stytch redirect URL. Fail loudly rather
  // than drift to another port if 5173 is taken.
  server: { port: 5173, strictPort: true },
})
