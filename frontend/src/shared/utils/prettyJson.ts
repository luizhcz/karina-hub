/**
 * Helpers para renderizar strings JSON com escapes (`á`, `\"`) decodificadas.
 *
 * O backend persiste alguns payloads em workflow_event_audit como string literal
 * (com escapes Unicode + dupla serialização do output). O frontend recebe essa
 * string sem parse, então os escapes aparecem na UI como `á` em vez de `á`.
 * `JSON.parse` decodifica; `JSON.stringify` em JS não re-escapa caracteres não-ASCII.
 */

/**
 * Pretty-print indentado para JsonViewer. Cai pra string original quando o
 * input não é JSON válido.
 */
export function prettyJsonString(input: string, indent = 2): string {
  try {
    return JSON.stringify(JSON.parse(input), null, indent)
  } catch {
    return input
  }
}

/**
 * Single-line para previews em tabela/timeline. Mantém o conteúdo compacto
 * mas com escapes decodificados.
 */
export function prettyJsonInline(input: string): string {
  try {
    return JSON.stringify(JSON.parse(input))
  } catch {
    return input
  }
}
