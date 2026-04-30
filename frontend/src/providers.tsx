import { Component, type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { router } from './router'
import { Toaster } from './shared/ui/Toaster'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

function ErrorFallback({ error }: { error: Error }) {
  return (
    <div className="flex items-center justify-center h-screen bg-bg-primary">
      <div className="bg-bg-secondary border border-red-500/30 rounded-xl p-8 max-w-md">
        <h2 className="text-lg font-semibold text-red-400 mb-2">Erro inesperado</h2>
        <p className="text-sm text-text-secondary mb-4">{error.message}</p>
        <button
          onClick={() => window.location.reload()}
          className="px-4 py-2 bg-accent-blue text-white rounded-lg text-sm"
        >
          Recarregar
        </button>
      </div>
    </div>
  )
}

class ErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state = { error: null as Error | null }
  static getDerivedStateFromError(error: Error) {
    return { error }
  }
  render() {
    if (this.state.error) return <ErrorFallback error={this.state.error} />
    return this.props.children
  }
}

export function AppProviders() {
  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
        <Toaster />
      </QueryClientProvider>
    </ErrorBoundary>
  )
}
