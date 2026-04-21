import { useState, useEffect, useRef, useCallback } from 'react'

interface UsePolledDataResult<T> {
  data: T | null
  loading: boolean
  error: Error | null
  refresh: () => void
}

export function usePolledData<T>(
  fetcher: () => Promise<T>,
  intervalMs: number,
  deps: unknown[] = []
): UsePolledDataResult<T> {
  const [data, setData] = useState<T | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<Error | null>(null)
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const fetcherRef = useRef(fetcher)
  const mountedRef = useRef(true)
  fetcherRef.current = fetcher

  const doFetch = useCallback(() => {
    fetcherRef.current()
      .then(result => {
        if (mountedRef.current) {
          setData(result)
          setError(null)
          setLoading(false)
        }
      })
      .catch(err => {
        if (mountedRef.current) {
          setError(err instanceof Error ? err : new Error(String(err)))
          setLoading(false)
        }
      })
  }, [])

  useEffect(() => {
    mountedRef.current = true
    // Don't reset data to null — keep stale data visible while refreshing
    // Only show loading on very first mount (data === null)
    doFetch()
    timerRef.current = setInterval(doFetch, intervalMs)
    return () => {
      mountedRef.current = false
      if (timerRef.current) clearInterval(timerRef.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [intervalMs, ...deps])

  return { data, loading: loading && data === null, error, refresh: doFetch }
}
