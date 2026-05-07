// @ts-check
/**
 * <efs-param-rows-editor> — KvTable especializado em path/query params.
 * Cada linha tem: name + type (string/number/integer/boolean) + description
 * + required flag + opcional `locked` (path param vindo da URL).
 *
 * Substitui o uso de KvTable<ParamRow> em mvp/src/routes/ToolEditor.tsx.
 *
 * Atributos:
 *   - locked-hint (string) — texto pra rows com locked=true (default 'vem da URL')
 *   - empty-hint (string)
 *
 * Propriedades JS:
 *   - value: Record<string, { type, description, required }> — converte de/pra
 *     o shape pathParams/queryParams do GenericTool
 *   - rows: Array<{ id, key, type, description, required, locked? }>
 *   - lockedKeys: string[] — chaves que devem ficar não-editáveis (path params
 *     auto-detectados via {placeholder} na URL)
 *
 * Eventos:
 *   - change (CustomEvent<Record<string, ParamDefinition>>)
 */

import { CloseIcon, PlusIcon } from '../lib/icons.js';
import { button, iconButton } from '../lib/ui.js';

const TYPE_OPTIONS = [
  { value: 'string', label: 'string' },
  { value: 'number', label: 'number' },
  { value: 'integer', label: 'integer' },
  { value: 'boolean', label: 'boolean' },
];

function shortId() {
  return Math.random().toString(36).slice(2, 10);
}

class EfsParamRowsEditor extends HTMLElement {
  constructor() {
    super();
    /** @type {Array<{ id: string, key: string, type: string, description: string, required: boolean, locked?: boolean }>} */
    this._rows = [];
    /** @type {Set<string>} */
    this._lockedKeys = new Set();
  }

  connectedCallback() {
    this._render();
  }

  /** @param {Record<string, { type: string, description: string, required: boolean }>} map */
  set value(map) {
    this._rows = Object.entries(map ?? {}).map(([key, def]) => ({
      id: shortId(),
      key,
      type: def.type ?? 'string',
      description: def.description ?? '',
      required: def.required ?? false,
      locked: this._lockedKeys.has(key),
    }));
    if (this.isConnected) this._render();
  }

  /** @returns {Record<string, { type: string, description: string, required: boolean }>} */
  get value() {
    /** @type {Record<string, any>} */
    const out = {};
    for (const r of this._rows) {
      const k = r.key.trim();
      if (!k) continue;
      out[k] = { type: r.type, description: r.description, required: r.required };
    }
    return out;
  }

  /** @param {string[]} keys */
  set lockedKeys(keys) {
    const set = new Set(keys ?? []);
    this._lockedKeys = set;
    // Garante que cada locked key esteja presente nas rows.
    for (const k of set) {
      if (!this._rows.some((r) => r.key === k)) {
        this._rows.push({ id: shortId(), key: k, type: 'string', description: '', required: true, locked: true });
      }
    }
    // Atualiza locked flag nas existentes.
    this._rows = this._rows.map((r) => ({ ...r, locked: set.has(r.key) }));
    if (this.isConnected) this._render();
  }

  _emit() {
    this.dispatchEvent(new CustomEvent('change', { detail: this.value, bubbles: true }));
  }

  _render() {
    const lockedHint = this.getAttribute('locked-hint') ?? 'vem da URL';
    const emptyHint = this.getAttribute('empty-hint') ?? 'Nenhum parâmetro ainda.';

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

    const baseInput = 'h-9 w-full rounded-lg border border-border bg-surface px-3 text-sm text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30';
    const monoInput = `${baseInput} font-mono text-[12px]`;

    const rowsHtml = this._rows.map((row) => {
      const lockedClass = row.locked ? 'opacity-60' : '';
      const typeOptions = TYPE_OPTIONS
        .map((o) => `<option value="${o.value}"${o.value === row.type ? ' selected' : ''}>${o.label}</option>`)
        .join('');

      return `
        <div class="rounded-lg border border-border bg-bg-soft p-3 ${lockedClass}" data-row-id="${escapeAttr(row.id)}">
          <div class="grid grid-cols-12 gap-2">
            <div class="col-span-12 sm:col-span-4">
              <input type="text" data-row-key value="${escapeAttr(row.key)}" placeholder="nome"
                     ${row.locked ? 'disabled' : ''}
                     class="${monoInput} ${row.locked ? 'cursor-not-allowed' : ''}" />
              ${row.locked ? `<span class="mt-1 block text-[10px] text-fg-dim">${escapeHtml(lockedHint)}</span>` : ''}
            </div>
            <div class="col-span-6 sm:col-span-3">
              <select data-row-type class="${baseInput}">${typeOptions}</select>
            </div>
            <div class="col-span-6 sm:col-span-4">
              <input type="text" data-row-description value="${escapeAttr(row.description)}" placeholder="descrição"
                     class="${baseInput}" />
            </div>
            <div class="col-span-12 sm:col-span-1 flex items-center justify-end gap-2">
              <label class="flex items-center gap-1 text-[11px] text-fg-muted">
                <input type="checkbox" data-row-required ${row.required ? 'checked' : ''}
                       class="h-3.5 w-3.5 rounded border-border" />
                req
              </label>
              ${
                row.locked
                  ? ''
                  : iconButton(CloseIcon('h-4 w-4'), { ariaLabel: 'Remover', extraClasses: 'text-danger hover:bg-danger/10', attrs: { 'data-row-remove': true } })
              }
            </div>
          </div>
        </div>
      `;
    }).join('');

    this.innerHTML = `
      <div class="space-y-2">
        ${rowsHtml}
        ${button({ label: 'Adicionar', variant: 'secondary', size: 'sm', leftIcon: PlusIcon('h-3.5 w-3.5'), attrs: { 'data-efs-add': true } })}
      </div>
    `;

    this.querySelectorAll('[data-row-id]').forEach((rowEl) => {
      const id = rowEl.getAttribute('data-row-id');
      if (!id) return;
      const findRow = () => this._rows.find((r) => r.id === id);

      const keyInput = /** @type {HTMLInputElement | null} */ (rowEl.querySelector('[data-row-key]'));
      keyInput?.addEventListener('input', () => {
        const row = findRow();
        if (row && !row.locked) { row.key = keyInput.value; this._emit(); }
      });
      const typeSel = /** @type {HTMLSelectElement | null} */ (rowEl.querySelector('[data-row-type]'));
      typeSel?.addEventListener('change', () => {
        const row = findRow(); if (row) { row.type = typeSel.value; this._emit(); }
      });
      const descInput = /** @type {HTMLInputElement | null} */ (rowEl.querySelector('[data-row-description]'));
      descInput?.addEventListener('input', () => {
        const row = findRow(); if (row) { row.description = descInput.value; this._emit(); }
      });
      const reqInput = /** @type {HTMLInputElement | null} */ (rowEl.querySelector('[data-row-required]'));
      reqInput?.addEventListener('change', () => {
        const row = findRow(); if (row) { row.required = reqInput.checked; this._emit(); }
      });
      rowEl.querySelector('[data-row-remove]')?.addEventListener('click', () => this._onRemove(id));
    });

    this.querySelector('[data-efs-add]')?.addEventListener('click', () => this._onAdd());
  }

  _onAdd() {
    this._rows.push({ id: shortId(), key: '', type: 'string', description: '', required: false });
    this._render();
    const last = this.querySelector('[data-row-id]:last-of-type [data-row-key]');
    /** @type {HTMLInputElement | null} */ (last)?.focus();
    this._emit();
  }

  /** @param {string} id */
  _onRemove(id) {
    const row = this._rows.find((r) => r.id === id);
    if (row?.locked) return;
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

customElements.define('efs-param-rows-editor', EfsParamRowsEditor);
