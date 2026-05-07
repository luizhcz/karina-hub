// @ts-check
/**
 * Helpers de UI que retornam HTML strings — primitivos sem state próprio.
 *
 * Pra primitivos com lifecycle (modal, drawer, KV editor) use Web Components
 * em /components/. Pra primitivos puros (button, input, select, badge),
 * estas funções renderizam markup com Tailwind direto. Listeners ficam no
 * caller via event delegation.
 *
 * Substitui mvp/src/ui/{Button, Input, Select, Textarea, Badge, IconButton,
 * ErrorMessage}.tsx em forma simplificada.
 */

import { escape as escapeHtml } from './dom.js';

// ── Button ────────────────────────────────────────────────────────────────

const BUTTON_VARIANTS = {
  primary: 'bg-accent text-accent-contrast hover:bg-accent-soft hover:shadow-soft focus-visible:ring-accent/40',
  secondary: 'border border-border bg-surface text-fg hover:bg-surface-hover focus-visible:ring-accent/30',
  ghost: 'text-fg-muted hover:bg-surface-hover hover:text-fg focus-visible:ring-accent/30',
  danger: 'bg-danger text-white hover:opacity-90 focus-visible:ring-danger/40',
};

const BUTTON_SIZES = {
  sm: 'h-8 rounded-md px-3 text-xs gap-1.5',
  md: 'h-9 rounded-lg px-4 text-sm gap-2',
  lg: 'h-11 rounded-lg px-5 text-sm gap-2',
};

/**
 * @typedef {object} ButtonOpts
 * @property {string} label Texto do botão
 * @property {keyof typeof BUTTON_VARIANTS} [variant='primary']
 * @property {keyof typeof BUTTON_SIZES} [size='md']
 * @property {string} [leftIcon] HTML/SVG inline raw
 * @property {string} [rightIcon] HTML/SVG inline raw
 * @property {boolean} [loading]
 * @property {boolean} [disabled]
 * @property {'submit' | 'button' | 'reset'} [type='button']
 * @property {string} [extraClasses]
 * @property {Record<string, string | number | boolean>} [attrs] Atributos extras (data-*, aria-*, id, etc)
 */

/** @param {ButtonOpts} opts */
export function button(opts) {
  const variant = opts.variant ?? 'primary';
  const size = opts.size ?? 'md';
  const type = opts.type ?? 'button';
  const disabled = opts.disabled || opts.loading;

  const classes = [
    'inline-flex items-center justify-center font-medium transition focus-visible:outline-none focus-visible:ring-2 disabled:cursor-not-allowed disabled:opacity-50',
    BUTTON_VARIANTS[variant],
    BUTTON_SIZES[size],
    opts.extraClasses ?? '',
  ].filter(Boolean).join(' ');

  const attrs = renderAttrs(opts.attrs ?? {});
  const leftIcon = opts.loading
    ? '<svg viewBox="0 0 24 24" fill="none" class="animate-spin h-4 w-4"><circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" class="opacity-20"/><path d="M22 12a10 10 0 0 1-10 10" stroke="currentColor" stroke-width="3" stroke-linecap="round"/></svg>'
    : (opts.leftIcon ?? '');
  const rightIcon = opts.loading ? '' : (opts.rightIcon ?? '');

  return `<button type="${type}" class="${classes}"${disabled ? ' disabled' : ''}${attrs}>
    ${leftIcon}
    <span>${escapeHtml(opts.label)}</span>
    ${rightIcon}
  </button>`;
}

// ── Input ─────────────────────────────────────────────────────────────────

/**
 * @typedef {object} InputFieldOpts
 * @property {string} [label]
 * @property {string} [hint]
 * @property {string} [error]
 * @property {string} [name]
 * @property {string} [id]
 * @property {string} [value]
 * @property {string} [placeholder]
 * @property {string} [type] text|email|password|number|search…
 * @property {string} [leftIcon] SVG inline pra leftAddon
 * @property {boolean} [monospace]
 * @property {boolean} [autofocus]
 * @property {boolean} [required]
 * @property {boolean} [disabled]
 * @property {string} [extraClasses]
 * @property {Record<string, string | number | boolean>} [attrs]
 */

/** @param {InputFieldOpts} opts */
export function inputField(opts) {
  const id = opts.id ?? `field-${Math.random().toString(36).slice(2, 9)}`;
  const showError = !!opts.error;
  const inputClasses = [
    'h-9 w-full rounded-lg border bg-surface px-3 text-sm text-fg placeholder:text-fg-dim',
    'focus:outline-none focus:ring-2 focus:ring-accent/30',
    showError ? 'border-danger/60' : 'border-border focus:border-accent',
    opts.leftIcon ? 'pl-9' : '',
    opts.monospace ? 'font-mono text-[12px]' : '',
    opts.extraClasses ?? '',
  ].filter(Boolean).join(' ');

  const inputAttrs = renderAttrs({
    id,
    name: opts.name ?? id,
    type: opts.type ?? 'text',
    value: opts.value ?? '',
    placeholder: opts.placeholder ?? '',
    ...(opts.autofocus ? { autofocus: true } : {}),
    ...(opts.required ? { required: true } : {}),
    ...(opts.disabled ? { disabled: true } : {}),
    ...(showError ? { 'aria-invalid': 'true' } : {}),
    ...(opts.attrs ?? {}),
  });

  return `<div class="flex flex-col gap-1">
    ${opts.label ? `<label for="${id}" class="text-xs font-medium text-fg-muted">${escapeHtml(opts.label)}</label>` : ''}
    <div class="relative">
      ${opts.leftIcon ? `<span class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">${opts.leftIcon}</span>` : ''}
      <input class="${inputClasses}"${inputAttrs} />
    </div>
    ${
      opts.error
        ? `<span class="text-[11px] text-danger">${escapeHtml(opts.error)}</span>`
        : opts.hint
          ? `<span class="text-[11px] text-fg-dim">${escapeHtml(opts.hint)}</span>`
          : ''
    }
  </div>`;
}

// ── Select ────────────────────────────────────────────────────────────────

/**
 * @typedef {{ value: string, label: string, disabled?: boolean }} SelectOption
 *
 * @typedef {object} SelectFieldOpts
 * @property {string} [label]
 * @property {string} [hint]
 * @property {string} [error]
 * @property {string} [name]
 * @property {string} [id]
 * @property {string} [value]
 * @property {SelectOption[]} options
 * @property {string} [placeholder]
 * @property {boolean} [disabled]
 * @property {boolean} [required]
 * @property {string} [extraClasses]
 * @property {Record<string, string | number | boolean>} [attrs]
 */

/** @param {SelectFieldOpts} opts */
export function selectField(opts) {
  const id = opts.id ?? `field-${Math.random().toString(36).slice(2, 9)}`;
  const showError = !!opts.error;
  const selectClasses = [
    'h-9 w-full rounded-lg border bg-surface px-3 text-sm text-fg',
    'focus:outline-none focus:ring-2 focus:ring-accent/30',
    showError ? 'border-danger/60' : 'border-border focus:border-accent',
    opts.disabled ? 'cursor-not-allowed opacity-60' : '',
    opts.extraClasses ?? '',
  ].filter(Boolean).join(' ');

  const optionsHtml = [
    opts.placeholder
      ? `<option value="" ${opts.value ? '' : 'selected'}>${escapeHtml(opts.placeholder)}</option>`
      : '',
    ...opts.options.map(
      (o) =>
        `<option value="${escapeHtml(o.value)}"${o.value === opts.value ? ' selected' : ''}${o.disabled ? ' disabled' : ''}>${escapeHtml(o.label)}</option>`,
    ),
  ].join('');

  const selectAttrs = renderAttrs({
    id,
    name: opts.name ?? id,
    ...(opts.disabled ? { disabled: true } : {}),
    ...(opts.required ? { required: true } : {}),
    ...(showError ? { 'aria-invalid': 'true' } : {}),
    ...(opts.attrs ?? {}),
  });

  return `<div class="flex flex-col gap-1">
    ${opts.label ? `<label for="${id}" class="text-xs font-medium text-fg-muted">${escapeHtml(opts.label)}</label>` : ''}
    <select class="${selectClasses}"${selectAttrs}>${optionsHtml}</select>
    ${
      opts.error
        ? `<span class="text-[11px] text-danger">${escapeHtml(opts.error)}</span>`
        : opts.hint
          ? `<span class="text-[11px] text-fg-dim">${escapeHtml(opts.hint)}</span>`
          : ''
    }
  </div>`;
}

// ── Textarea ──────────────────────────────────────────────────────────────

/**
 * @typedef {object} TextareaFieldOpts
 * @property {string} [label]
 * @property {string} [hint]
 * @property {string} [error]
 * @property {string} [name]
 * @property {string} [id]
 * @property {string} [value]
 * @property {string} [placeholder]
 * @property {number} [rows]
 * @property {boolean} [monospace]
 * @property {boolean} [required]
 * @property {boolean} [disabled]
 * @property {string} [extraClasses]
 * @property {Record<string, string | number | boolean>} [attrs]
 */

/** @param {TextareaFieldOpts} opts */
export function textareaField(opts) {
  const id = opts.id ?? `field-${Math.random().toString(36).slice(2, 9)}`;
  const showError = !!opts.error;
  const taClasses = [
    'w-full rounded-lg border bg-surface px-3 py-2 text-sm text-fg placeholder:text-fg-dim',
    'focus:outline-none focus:ring-2 focus:ring-accent/30',
    showError ? 'border-danger/60' : 'border-border focus:border-accent',
    opts.monospace ? 'font-mono text-[12px]' : '',
    opts.extraClasses ?? '',
  ].filter(Boolean).join(' ');

  const taAttrs = renderAttrs({
    id,
    name: opts.name ?? id,
    rows: opts.rows ?? 4,
    placeholder: opts.placeholder ?? '',
    ...(opts.required ? { required: true } : {}),
    ...(opts.disabled ? { disabled: true } : {}),
    ...(showError ? { 'aria-invalid': 'true' } : {}),
    ...(opts.attrs ?? {}),
  });

  return `<div class="flex flex-col gap-1">
    ${opts.label ? `<label for="${id}" class="text-xs font-medium text-fg-muted">${escapeHtml(opts.label)}</label>` : ''}
    <textarea class="${taClasses}"${taAttrs}>${escapeHtml(opts.value ?? '')}</textarea>
    ${
      opts.error
        ? `<span class="text-[11px] text-danger">${escapeHtml(opts.error)}</span>`
        : opts.hint
          ? `<span class="text-[11px] text-fg-dim">${escapeHtml(opts.hint)}</span>`
          : ''
    }
  </div>`;
}

// ── Badge ─────────────────────────────────────────────────────────────────

const BADGE_TONES = {
  neutral: 'border-border bg-surface text-fg-muted',
  accent: 'border-accent/40 bg-accent-subtle text-accent',
  success: 'border-success/40 bg-success/10 text-success',
  warning: 'border-warning/40 bg-warning/10 text-warning',
  danger: 'border-danger/40 bg-danger/10 text-danger',
};

/**
 * @param {string} text
 * @param {{ tone?: keyof typeof BADGE_TONES, extraClasses?: string }} [opts]
 */
export function badge(text, opts) {
  const tone = opts?.tone ?? 'neutral';
  const classes = [
    'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[10px] font-medium',
    BADGE_TONES[tone],
    opts?.extraClasses ?? '',
  ].filter(Boolean).join(' ');
  return `<span class="${classes}">${escapeHtml(text)}</span>`;
}

// ── Icon Button ───────────────────────────────────────────────────────────

/**
 * @param {string} iconHtml SVG inline
 * @param {{ ariaLabel: string, title?: string, extraClasses?: string, attrs?: Record<string, string | number | boolean> }} opts
 */
export function iconButton(iconHtml, opts) {
  const classes = [
    'inline-flex h-8 w-8 items-center justify-center rounded-md text-fg-muted hover:bg-surface-hover hover:text-fg transition-colors',
    opts.extraClasses ?? '',
  ].filter(Boolean).join(' ');
  const attrs = renderAttrs({
    type: 'button',
    'aria-label': opts.ariaLabel,
    ...(opts.title ? { title: opts.title } : {}),
    ...(opts.attrs ?? {}),
  });
  return `<button class="${classes}"${attrs}>${iconHtml}</button>`;
}

// ── Error message ─────────────────────────────────────────────────────────

/** @param {string} message */
export function errorMessage(message) {
  return `<p class="rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">${escapeHtml(message)}</p>`;
}

// ── Helper: serializa atributos ───────────────────────────────────────────

/**
 * @param {Record<string, string | number | boolean>} attrs
 * @returns {string}
 */
function renderAttrs(attrs) {
  const parts = [];
  for (const [key, value] of Object.entries(attrs)) {
    if (value === false || value === undefined || value === null) continue;
    if (value === true) {
      parts.push(` ${key}`);
    } else if (value === '') {
      // Pula vazios pra não poluir output (placeholder="" não tem valor).
      // Exceção: se o caller realmente quer attribute vazio, usar attrs explícito.
      continue;
    } else {
      parts.push(` ${key}="${escapeHtml(String(value))}"`);
    }
  }
  return parts.join('');
}
