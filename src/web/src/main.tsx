import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { App } from './App'
import { AuthProvider } from './auth/AuthProvider'
import { apiRetryDelay, shouldRetryApiQuery } from './lib/api'
import './styles.css'

const root = document.getElementById('root')

if (!root) {
  throw new Error('Missing application root')
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: shouldRetryApiQuery,
      retryDelay: apiRetryDelay,
      refetchOnWindowFocus: false,
    },
    mutations: { retry: 0 },
  },
})

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider><App /></AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
