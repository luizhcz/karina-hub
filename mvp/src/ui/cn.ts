// Helper minimalista pra concatenar classes condicionais. Evita dependência de
// `clsx`/`classnames` — escopo do MVP não precisa.
export function cn(...args: Array<string | false | null | undefined>): string {
  return args.filter(Boolean).join(' ')
}
