import type { Config } from 'tailwindcss'

// Paleta semântica via CSS variables — tokens definidos em src/index.css em
// :root (light) e .dark. Permite trocar cor base sem alterar componentes.
const tokenColor = (name: string) => `rgb(var(${name}) / <alpha-value>)`

export default {
  darkMode: 'class',
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },
      colors: {
        // Tokens semânticos — preferir esses ao usar Tailwind nos componentes.
        bg: tokenColor('--color-bg'),
        'bg-soft': tokenColor('--color-bg-soft'),
        surface: tokenColor('--color-surface'),
        'surface-hover': tokenColor('--color-surface-hover'),
        border: tokenColor('--color-border'),
        'border-strong': tokenColor('--color-border-strong'),
        fg: tokenColor('--color-fg'),
        'fg-muted': tokenColor('--color-fg-muted'),
        'fg-dim': tokenColor('--color-fg-dim'),
        accent: {
          DEFAULT: tokenColor('--color-accent'),
          soft: tokenColor('--color-accent-soft'),
          contrast: tokenColor('--color-accent-contrast'),
          subtle: tokenColor('--color-accent-subtle'),
        },
        success: tokenColor('--color-success'),
        warning: tokenColor('--color-warning'),
        danger: tokenColor('--color-danger'),
      },
      boxShadow: {
        // Sombra "soft" reduzida pra estilo BTG mais minimalista: em light
        // mode é navy a 18% (sutil), em dark mode é branco a 18% (glow leve
        // mas não estridente).
        soft: '0 6px 20px -10px rgb(var(--color-accent) / 0.18)',
        card: '0 1px 0 rgb(var(--color-border) / 0.6) inset, 0 4px 12px -4px rgb(0 0 0 / 0.06)',
      },
    },
  },
  plugins: [],
} satisfies Config
