// Helper minimalista pra concatenar classes condicionais. Evita dep externa
// (clsx/classnames) — escopo do app não exige merge inteligente de Tailwind.
export function cn(...args: Array<string | false | null | undefined>): string {
  return args.filter(Boolean).join(' ')
}
