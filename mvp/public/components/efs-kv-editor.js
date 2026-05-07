// @ts-check
/**
 * <efs-kv-editor> — editor de pares chave/valor (key/val). Substitui
 * mvp/src/components/PostmanEditor/KvTable.tsx no caso simples (val=string).
 *
 * Atributos:
 *   - key-placeholder (string) — header da coluna chave (default 'chave')
 *   - val-label (string) — header da coluna valor (default 'valor')
 *   - val-placeholder (string) — placeholder do input de valor
 *   - empty-hint (string) — texto quando lista vazia
 *
 * Propriedades JS:
 *   - value: Record<string, string> — mapa atual; pode ser setado e lido
 *   - rows: Array<{id, key, val, locked?}> — versão estruturada se quiser locked
 *   - forbiddenKeys: string[] — chaves reservadas (vermelhas)
 *
 * Eventos:
 *   - change (CustomEvent<Record<string, string>>)
 *
 * Limitações: val é sempre string. Pra val complexo (param schemas), use
 * componente dedicado ou estenda este.
 */

import { CloseIcon, PlusIcon } from '../lib/icons.js';
import { button, iconButton } from '../lib/ui.js';

function shortId() {
  return Math.random().toString(36).slice(2, 10);
}

class EfsKvEditor extends HTMLElement {
  constructor() {
    super();
    /** @type {Array<{ id: string, key: string, val: string, locked?: boolean }>} */
    this._rows = [];
    /** @type {Set<string>} */
    this._forbidden = new Set();
  }

  connectedCallback() {
    this._render();
  }

  /** @param {Record<string, string>} map */
  set value(map) {
    this._rows = Object.entries(map ?? {}).map(([key, val]) => ({ id: shortId(), key, val }));
    if (this.isConnected) this._render();
  }

  /** @returns {Record<string, string>} */
  get value() {
    /** @type {Record<string, string>} */
    const out = {};
    for (const r of this._rows) {
      const k = r.key.trim();
      if (!k) continue;
      out[k] = r.val;
    }
    return out;
  }

  /** @param {Array<{id?: string, key: string, val: string, locked?: boolean}>} rows */
  set rows(rows) {
    this._rows = rows.map((r) => ({
      id: r.id ?? shortId(),
      key: r.key,
      val: r.val,
      locked: r.locked,
    }));
    if (this.isConnected) this._render();
  }

  /** @returns {Array<{ id: string, key: string, val: string, locked?: boolean }>} */
  get rows() {
    return [...this._rows];
  }

  /** @param {string[]} keys */
  set forbiddenKeys(keys) {
    this._forbidden = new Set((keys ?? []).map((k) => k.toLowerCase()));
    if (this.isConnected) this._render();
  }

  _emit() {
    this.dispatchEvent(new CustomEvent('change', { detail: this.value, bubbles: true }));
  }

  _render() {
    const keyPlaceholder = this.getAttribute('key-placeholder') ?? 'chave';
    const valLabel = this.getAttribute('val-label') ?? 'valor';
    const valPlaceholder = this.getAttribute('val-placeholder') ?? '';
    const emptyHint = this.getAttribute('empty-hint') ?? 'Nenhum item ainda.';

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

    const inputClassesBase = 'h-9 w-full rounded-lg border bg-surface px-3 font-mono text-[12px] text-fg placeholder:text-fg-dim focus:outline-none focus:ring-2 focus:ring-accent/30';

    const header = `
      <div class="grid grid-cols-12 gap-2 px-2 text-[10px] uppercase tracking-wider text-fg-dim">
        <div class="col-span-4">${escapeHtml(keyPlaceholder)}</div>
        <div class="col-span-7">${escapeHtml(valLabel)}</div>
        <div class="col-span-1"></div>
      </div>
    `;

    const rowsHtml = this._rows.map((row) => {
      const trimmed = row.key.trim().toLowerCase();
      const isForbidden = trimmed.length > 0 && this._forbidden.has(trimmed);
      const keyBorder = isForbidden ? 'border-danger/60' : 'border-border focus:border-accent';
      const lockedClass = row.locked ? 'cursor-not-allowed opacity-60' : '';
      return `
        <div class="grid grid-cols-12 items-start gap-2" data-row-id="${escapeAttr(row.id)}">
          <div class="col-span-4 flex flex-col gap-1">
            <input type="text" data-row-key value="${escapeAttr(row.key)}"
                   placeholder="${escapeAttr(keyPlaceholder)}"
                   ${row.locked ? 'disabled' : ''}
                   class="${inputClassesBase} ${keyBorder} ${lockedClass}" />
            ${isForbidden ? '<span class="text-[11px] text-danger">reservada</span>' : ''}
            ${row.locked ? '<span class="text-[11px] text-fg-dim">vem da URL</span>' : ''}
          </div>
          <div class="col-span-7">
            <input type="text" data-row-val value="${escapeAttr(row.val)}"
                   placeholder="${escapeAttr(valPlaceholder)}"
                   class="${inputClassesBase} border-border focus:border-accent" />
          </div>
          <div class="col-span-1 flex justify-end pt-1">
            ${iconButton(CloseIcon('h-4 w-4'), { ariaLabel: 'Remover linha', extraClasses: 'text-danger hover:bg-danger/10', attrs: { 'data-row-remove': true } })}
          </div>
        </div>
      `;
    }).join('');

    this.innerHTML = `
      <div class="space-y-2">
        ${header}
        ${rowsHtml}
        ${button({ label: 'Adicionar', variant: 'secondary', size: 'sm', leftIcon: PlusIcon('h-3.5 w-3.5'), attrs: { 'data-efs-add': true } })}
      </div>
    `;

    this.querySelectorAll('[data-row-id]').forEach((rowEl) => {
      const id = rowEl.getAttribute('data-row-id');
      if (!id) return;
      const keyInput = /** @type {HTMLInputElement | null} */ (rowEl.querySelector('[data-row-key]'));
      const valInput = /** @type {HTMLInputElement | null} */ (rowEl.querySelector('[data-row-val]'));
      keyInput?.addEventListener('input', () => {
        const row = this._rows.find((r) => r.id === id);
        if (row && !row.locked) {
          row.key = keyInput.value;
          this._emit();
        }
      });
      valInput?.addEventListener('input', () => {
        const row = this._rows.find((r) => r.id === id);
        if (row) {
          row.val = valInput.value;
          this._emit();
        }
      });
      rowEl.querySelector('[data-row-remove]')?.addEventListener('click', () => this._onRemove(id));
    });

    this.querySelector('[data-efs-add]')?.addEventListener('click', () => this._onAdd());
  }

  _onAdd() {
    this._rows.push({ id: shortId(), key: '', val: '' });
    this._render();
    const last = this.querySelector('[data-row-id]:last-of-type [data-row-key]');
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

customElements.define('efs-kv-editor', EfsKvEditor);
