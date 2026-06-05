import { useState } from 'react'
import { cn } from '../ui/cn'
import { friendlyError } from '../api/client'
import {
  deleteMessageFeedback,
  submitMessageFeedback,
  type MessageFeedbackSentiment,
} from '../api/messageFeedback'

interface Props {
  conversationId: string
  messageId: string
  initialSentiment?: MessageFeedbackSentiment | null
}

/**
 * Like/dislike de uma resposta do assistant. Click no mesmo sentimento desfaz
 * (DELETE); click no oposto sobrescreve via upsert. Estado otimista local —
 * em erro reverte e expõe o motivo via title (tooltip nativo).
 */
export function MessageFeedbackButtons({
  conversationId,
  messageId,
  initialSentiment = null,
}: Props) {
  const [sentiment, setSentiment] = useState<MessageFeedbackSentiment | null>(initialSentiment)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function apply(next: MessageFeedbackSentiment) {
    if (pending) return
    const previous = sentiment
    setError(null)
    setPending(true)
    try {
      if (previous === next) {
        setSentiment(null)
        await deleteMessageFeedback(conversationId, messageId)
      } else {
        setSentiment(next)
        await submitMessageFeedback(conversationId, messageId, { sentiment: next })
      }
    } catch (e) {
      setSentiment(previous)
      setError(friendlyError(e, 'Falha ao registrar feedback.'))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mt-1.5 flex items-center gap-1" title={error ?? undefined}>
      <button
        type="button"
        aria-label="Curtir resposta"
        aria-pressed={sentiment === 1}
        disabled={pending}
        onClick={() => void apply(1)}
        className={cn(
          'rounded p-1 transition-colors disabled:opacity-50',
          sentiment === 1
            ? 'text-emerald-500 bg-emerald-500/10'
            : 'text-fg-muted hover:text-fg hover:bg-bg-soft',
        )}
      >
        <ThumbsUpIcon filled={sentiment === 1} />
      </button>
      <button
        type="button"
        aria-label="Não curtir resposta"
        aria-pressed={sentiment === -1}
        disabled={pending}
        onClick={() => void apply(-1)}
        className={cn(
          'rounded p-1 transition-colors disabled:opacity-50',
          sentiment === -1
            ? 'text-rose-500 bg-rose-500/10'
            : 'text-fg-muted hover:text-fg hover:bg-bg-soft',
        )}
      >
        <ThumbsDownIcon filled={sentiment === -1} />
      </button>
    </div>
  )
}

function ThumbsUpIcon({ filled }: { filled: boolean }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill={filled ? 'currentColor' : 'none'}
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M7 10v12" />
      <path d="M15 5.88 14 10h5.83a2 2 0 0 1 1.92 2.56l-2.33 8A2 2 0 0 1 17.5 22H7V10l4.34-9.66a1.5 1.5 0 0 1 2.83.84z" />
    </svg>
  )
}

function ThumbsDownIcon({ filled }: { filled: boolean }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill={filled ? 'currentColor' : 'none'}
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M17 14V2" />
      <path d="M9 18.12 10 14H4.17a2 2 0 0 1-1.92-2.56l2.33-8A2 2 0 0 1 6.5 2H17v12l-4.34 9.66a1.5 1.5 0 0 1-2.83-.84z" />
    </svg>
  )
}
