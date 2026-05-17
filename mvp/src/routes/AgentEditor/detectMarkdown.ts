// Heurística pra detectar se um trecho de texto é Markdown. Pensada pra
// o cenário "user copia de um doc/notion e cola no editor" — favorece
// recall (não perder Markdown legítimo) sobre precisão extrema, mas
// evita disparar com sinais fracos isolados (um asterisco perdido).
//
// Sinais "fortes" (qualquer um basta): heading, fence de código, tabela
// com separador. Sinais "médios" (precisa de 2 ou mais): listas,
// negrito, blockquote, link. Mantém a função pura — sem efeitos
// colaterais, pra ficar trivial de testar e reutilizar.
export function looksLikeMarkdown(text: string): boolean {
  if (!text || text.length < 4) return false

  if (/^#{1,6}\s+\S/m.test(text)) return true
  if (/^```/m.test(text)) return true
  if (/^\|.+\|$/m.test(text) && /\|\s*[-:]+\s*\|/.test(text)) return true

  let medium = 0
  if (/^[-*+]\s+\S/m.test(text)) medium++
  if (/^\d+\.\s+\S/m.test(text)) medium++
  if (/\*\*[^*\n]+\*\*/.test(text)) medium++
  if (/^>\s+\S/m.test(text)) medium++
  if (/\[[^\]]+\]\([^)]+\)/.test(text)) medium++
  return medium >= 2
}
