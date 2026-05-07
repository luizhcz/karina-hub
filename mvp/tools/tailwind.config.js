// Paleta semântica via CSS variables — tokens definidos em
// mvp/public/css/tokens.css em :root (light) e .dark. Permite trocar cor base
// sem alterar componentes.
//
// Roda manualmente via mvp/tools/build-css.sh quando o design muda. NÃO faz
// parte do CI/Docker — output (mvp/public/css/utilities.css) é versionado.

const tokenColor = (name) => `rgb(var(${name}) / <alpha-value>)`;

/** @type {import('tailwindcss').Config} */
module.exports = {
  darkMode: 'class',
  // Após cleanup React (Fase 5), Tailwind escaneia só os vanilla HTML + JS.
  content: ['../public/**/*.{html,js}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },
      colors: {
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
        soft: '0 6px 20px -10px rgb(var(--color-accent) / 0.18)',
        card: '0 1px 0 rgb(var(--color-border) / 0.6) inset, 0 4px 12px -4px rgb(0 0 0 / 0.06)',
      },
    },
  },
  plugins: [],
};
