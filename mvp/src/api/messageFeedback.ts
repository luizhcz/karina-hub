import { post, del } from './client'

export type MessageFeedbackSentiment = 1 | -1

export interface MessageFeedback {
  feedbackId: string
  messageId: string
  conversationId: string
  sentiment: MessageFeedbackSentiment
  comment?: string | null
  createdAt: string
  updatedAt?: string | null
}

export function submitMessageFeedback(
  conversationId: string,
  messageId: string,
  body: { sentiment: MessageFeedbackSentiment; comment?: string | null },
): Promise<MessageFeedback> {
  return post<MessageFeedback>(
    `/conversations/${conversationId}/messages/${messageId}/feedback`,
    body,
  )
}

export function deleteMessageFeedback(
  conversationId: string,
  messageId: string,
): Promise<void> {
  return del<void>(`/conversations/${conversationId}/messages/${messageId}/feedback`)
}
