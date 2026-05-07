// @ts-check
/**
 * <efs-string-list-editor> — editor de lista plana de strings com add/remove.
 * Substitui mvp/src/ui/StringListEditor.tsx.
 *
 * Atributos:
 *   - placeholder (string) — placeholder dos inputs
 *   - empty-hint (string) — hint quando lista vazia
 *   - monospace (boolean) — fonte monospace nos inputs
 *
 * Propriedades JS (use via element.value = [...]):
 *   - value: string[] — lista atual (filtra vazios automaticamente no get)
 *
 * Eventos:
 *   - change (CustomEvent<string[]>) — disparado quando lista muda
 *
 * Uso:
 *   const list = document.querySelector('efs-string-list-editor');
 *   list.value = ['tool1', 'tool2'];
 *   list.addEventListener('change', (e) => console.log(e.detail));
 */

import { CloseIcon, PlusIcon } from '../lib/icons.js';
import { button, iconButton } from '../lib/ui.js';

function shortId() {
  return Math.random().toString(36).slice(2, 10);
}

class EfsStringListEditor extends HTMLElement {
  constructor() {
    super();
    /** @type {Array<{ id: string, value: string }>} */
    this._rows = [];
  }

  connectedCallback() {
    this._render();
  }

  /** @param {string[]} list */
  set value(list) {
    this._rows = (list ?? []).map((value) => ({ id: shortId(), value }));
    if (this.isConnected) this._render();
  }

  /** @returns {string[]} */
  get value() {
    return this._rows.map((r) => r.value.trim()).filter((v) => v.length > 0);
  }

  _emit() {
    this.dispatchEvent(new CustomEvent('change', { detail: this.value, bubbles: true }));
  }

  _render() {
    const placeholder = this.getAttribute('placeholder') ?? 'item';
    const emptyHint = this.getAttribute('empty-hint') ?? 'Nenhum item ainda.';
    const monospace = this.hasAttribute('monospace');

    if (this._rows.length === 0) {
      this.innerHTML = `
        <div class="space-y-3">
          <div class="rounded-lg border border-dashed border-border px-4 py-6 text-center text-xs text-fg-muted">
            ${escapeHtml(emptyHint)}
          </div>
          ${button({ label: 'Adicionar', variant: 'secondary', size: 'sm', leftIcon: PlusIcon('h-3.5 w-3.5'), attrs: { 'data-efs-add': true } })}
        </div>
      `;
      this.querySelector('[data-efs-add]')?.addEventListener('click', () => this._onAdd());
      return;
    }

    const inputClasses = `h-9 w-full rounded-lg border border-border bg-surface px-3 text-sm text-fg placeholder:text-fg-dim focus:outline-none focus:ring-2 focus:ring-accent/30 focus:border-accent${monospace ? ' font-mono text-[12px]' : ''}`;

    const rowsHtml = this._rows.map((row) => `
      <div class="flex items-start gap-2" data-row-id="${escapeAttr(row.id)}">
        <div class="flex-1">
          <input type="text" value="${escapeAttr(row.value)}" placeholder="${escapeAttr(placeholder)}"
                 class="${inputClasses}" data-row-input />
        </div>
        ${iconButton(CloseIcon('h-4 w-4'), { ariaLabel: 'Remover item', extraClasses: 'text-danger hover:bg-danger/10', attrs: { 'data-row-remove': true } })}
      </div>
    `).join('');

    this.innerHTML = `
      <div class="space-y-2">
        ${rowsHtml}
        ${button({ label: 'Adicionar', variant: 'secondary', size: 'sm', leftIcon: PlusIcon('h-3.5 w-3.5'), attrs: { 'data-efs-add': true } })}
      </div>
    `;

    this.querySelectorAll('[data-row-id]').forEach((rowEl) => {
      const id = rowEl.getAttribute('data-row-id');
      if (!id) return;
      const input = rowEl.querySelector('[data-row-input]');
      input?.addEventListener('input', (e) => {
        const target = /** @type {HTMLInputElement} */ (e.currentTarget);
        const row = this._rows.find((r) => r.id === id);
        if (row) {
          row.value = target.value;
          this._emit();
        }
      });
      rowEl.querySelector('[data-row-remove]')?.addEventListener('click', () => this._onRemove(id));
    });

    this.querySelector('[data-efs-add]')?.addEventListener('click', () => this._onAdd());
  }

  _onAdd() {
    this._rows.push({ id: shortId(), value: '' });
    this._render();
    // Foca o input que acabou de aparecer.
    const last = this.querySelector('[data-row-id]:last-child [data-row-input]');
    /** @type {HTMLInputElement | null} */ (last)?.focus();
    this._emit();
  }

  /** @param {string} id */
  _onRemove(id) {
    this._rows = this._rows.filter((r) => r.id !== id);
    this._render();
    this._emit();
  }
}

/** @param {string} v */
function escapeHtml(v) {
  return String(v ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
/** @param {string} v */
function escapeAttr(v) { return escapeHtml(v); }

customElements.define('efs-string-list-editor', EfsStringListEditor);
