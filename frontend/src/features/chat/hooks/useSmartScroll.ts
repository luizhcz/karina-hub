import { useRef, useState, useCallback, useEffect } from 'react'

const THRESHOLD = 100

export function useSmartScroll(deps: unknown[]) {
  const containerRef = useRef<HTMLDivElement>(null)
  const bottomRef = useRef<HTMLDivElement>(null)
  const [isAtBottom, setIsAtBottom] = useState(true)
  const [unreadCount, setUnreadCount] = useState(0)

  const checkAtBottom = useCallback(() => {
    const el = containerRef.current
    if (!el) return true
    return el.scrollHeight - el.scrollTop - el.clientHeight < THRESHOLD
  }, [])

  const handleScroll = useCallback(() => {
    const atBottom = checkAtBottom()
    setIsAtBottom(atBottom)
    if (atBottom) setUnreadCount(0)
  }, [checkAtBottom])

  const scrollToBottom = useCallback(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
    setUnreadCount(0)
    setIsAtBottom(true)
  }, [])

  // Auto-scroll or increment unread when deps change
  useEffect(() => {
    if (checkAtBottom()) {
      bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
    } else {
      setUnreadCount((c) => c + 1)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  return { containerRef, bottomRef, isAtBottom, unreadCount, handleScroll, scrollToBottom }
}
