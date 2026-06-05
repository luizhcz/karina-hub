import { useState } from 'react'
import { useSubmitMessageFeedback, useDeleteMessageFeedback } from '../../../api/chat'

type Sentiment = 1 | -1

interface MessageFeedbackButtonsProps {
  conversationId: string
  messageId: string
  initialSentiment?: Sentiment | null
}

/**
 * Botões de like/dislike para uma mensagem de assistente.
 * - Click no botão ativo (mesmo sentimento) → remove o feedback.
 * - Click no oposto → upsert (sobrescreve).
 * Estado otimista local; em erro reverte e mostra título com mensagem.
 */
export function MessageFeedbackButtons({
  conversationId,
  messageId,
  initialSentiment = null,
}: MessageFeedbackButtonsProps) {
  const [sentiment, setSentiment] = useState<Sentiment | null>(initialSentiment)
  const [error, setError] = useState<string | null>(null)
  const submit = useSubmitMessageFeedback()
  const remove = useDeleteMessageFeedback()

  const pending = submit.isPending || remove.isPending

  const apply = async (next: Sentiment) => {
    if (pending) return
    setError(null)
    const previous = sentiment
    if (sentiment === next) {
      setSentiment(null)
      try {
        await remove.mutateAsync({ conversationId, messageId })
      } catch (e) {
        setSentiment(previous)
        setError((e as Error)?.message ?? 'Falha ao remover feedback')
      }
      return
    }
    setSentiment(next)
    try {
      await submit.mutateAsync({ conversationId, messageId, sentiment: next })
    } catch (e) {
      setSentiment(previous)
      setError((e as Error)?.message ?? 'Falha ao enviar feedback')
    }
  }

  const baseBtn = 'rounded p-1 text-xs transition-colors disabled:opacity-50'
  const idle = 'text-text-muted hover:text-text-primary hover:bg-white/5'
  const activeLike = 'text-emerald-400 bg-emerald-400/10'
  const activeDislike = 'text-rose-400 bg-rose-400/10'

  return (
    <div className="mt-1.5 flex items-center gap-1" title={error ?? undefined}>
      <button
        type="button"
        aria-label="Curtir esta resposta"
        aria-pressed={sentiment === 1}
        disabled={pending}
        onClick={() => apply(1)}
        className={`${baseBtn} ${sentiment === 1 ? activeLike : idle}`}
      >
        <ThumbsUpIcon filled={sentiment === 1} />
      </button>
      <button
        type="button"
        aria-label="Não curtir esta resposta"
        aria-pressed={sentiment === -1}
        disabled={pending}
        onClick={() => apply(-1)}
        className={`${baseBtn} ${sentiment === -1 ? activeDislike : idle}`}
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
