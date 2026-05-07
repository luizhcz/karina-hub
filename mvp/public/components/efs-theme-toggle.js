// @ts-check
/**
 * <efs-theme-toggle> — botão de alternar light/dark. Substitui
 * mvp/src/ui/ThemeToggle.tsx.
 *
 * Auto-renderiza o ícone correto (Sun em dark, Moon em light) e re-renderiza
 * quando o tema mudar (escuta evento `efs-theme-changed` no window).
 *
 * Uso:
 *   <efs-theme-toggle></efs-theme-toggle>
 */

import { getTheme, toggleTheme } from '../lib/theme.js';
import { MoonIcon, SunIcon } from '../lib/icons.js';

class EfsThemeToggle extends HTMLElement {
  constructor() {
    super();
    this._onThemeChanged = this._onThemeChanged.bind(this);
    this._onClick = this._onClick.bind(this);
  }

  connectedCallback() {
    this._render();
    window.addEventListener('efs-theme-changed', this._onThemeChanged);
  }

  disconnectedCallback() {
    window.removeEventListener('efs-theme-changed', this._onThemeChanged);
  }

  _onThemeChanged() {
    this._render();
  }

  _onClick() {
    toggleTheme();
  }

  _render() {
    const isDark = getTheme() === 'dark';
    const label = isDark ? 'Mudar para tema claro' : 'Mudar para tema escuro';
    const title = isDark ? 'Tema claro' : 'Tema escuro';
    const icon = isDark ? SunIcon('h-4 w-4') : MoonIcon('h-4 w-4');

    this.innerHTML = `
      <button type="button" aria-label="${label}" title="${title}"
              class="inline-flex h-9 w-9 items-center justify-center rounded-lg text-fg-muted hover:bg-surface-hover hover:text-fg transition-colors">
        ${icon}
      </button>
    `;
    this.querySelector('button')?.addEventListener('click', this._onClick);
  }
}

customElements.define('efs-theme-toggle', EfsThemeToggle);
