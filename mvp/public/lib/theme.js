// @ts-check
/**
 * Tema light/dark persistido em localStorage. Aplica `<html class="dark">`
 * pra ativar tokens definidos em css/tokens.css. Substitui
 * mvp/src/theme/ThemeProvider.tsx.
 *
 * Auto-aplica o tema correto na carga (chamada explícita ao import). Use
 * apenas em páginas vanilla — páginas React continuam tendo seu próprio
 * provider durante a transição (ambos compartilham a chave 'efs-mvp-theme'
 * e a classe `dark`, então alternar tema persiste entre rotas migradas e
 * não-migradas).
 */

const STORAGE_KEY = 'efs-mvp-theme';

/** @returns {'light' | 'dark'} */
function detectInitial() {
  if (typeof window === 'undefined') return 'dark';
  const stored = window.localStorage.getItem(STORAGE_KEY);
  if (stored === 'light' || stored === 'dark') return stored;
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/** @param {'light' | 'dark'} theme */
function applyToDom(theme) {
  const root = document.documentElement;
  root.classList.toggle('dark', theme === 'dark');
  // Hint pro browser ajustar barras de rolagem e form controls nativos.
  root.style.colorScheme = theme;
}

/** @returns {'light' | 'dark'} */
export function getTheme() {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}

/** @param {'light' | 'dark'} theme */
export function setTheme(theme) {
  window.localStorage.setItem(STORAGE_KEY, theme);
  applyToDom(theme);
  // Notifica outros consumers (ex: efs-theme-toggle re-render do ícone).
  window.dispatchEvent(new CustomEvent('efs-theme-changed', { detail: theme }));
}

export function toggleTheme() {
  setTheme(getTheme() === 'dark' ? 'light' : 'dark');
}

// Auto-aplica no carregamento do módulo. Faz com que o tema correto já
// esteja ativo antes do primeiro paint quando o módulo é importado no <head>
// ou no início do body.
applyToDom(detectInitial());
