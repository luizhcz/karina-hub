// @ts-check
/**
 * <efs-drawer> — drawer lateral fixo à direita. Pattern usado em
 * Avaliacoes/RunDetailDrawer + GlossaryDrawer + AssistantDrawer.
 *
 * Atributos:
 *   - open (boolean) — visibilidade
 *   - title (string) — título no header
 *   - description (string) — subtítulo
 *   - eyebrow (string) — texto pequeno em uppercase acima do título
 *   - width (string) — largura em CSS (default '640px')
 *   - z-index (string) — default '40' (drawers aninhados aumentam pra 50, 60…)
 *
 * Children: tudo dentro do custom element vai pro body do drawer.
 *   Use [data-efs-footer] pra área inferior fixa (tipo botões de ação).
 *   Use [data-efs-header-actions] pra botões à direita do título.
 *
 * Eventos:
 *   - close (CustomEvent) — disparado em ESC, click no overlay ou botão X.
 *
 * Uso:
 *   <efs-drawer open title="Detalhes" eyebrow="Avaliação" width="640px">
 *     <div data-efs-header-actions><button>Glossário</button></div>
 *     <p>conteúdo...</p>
 *     <div data-efs-footer><button>Cancelar</button></div>
 *   </efs-drawer>
 */

import { CloseIcon } from '../lib/icons.js';
import { iconButton } from '../lib/ui.js';

class EfsDrawer extends HTMLElement {
  static get observedAttributes() {
    return ['open', 'title', 'description', 'eyebrow', 'width', 'z-index'];
  }

  constructor() {
    super();
    /** @type {string | null} */
    this._previousOverflow = null;
    /** @type {Node[]} */
    this._bodyContent = [];
    /** @type {Node[]} */
    this._footerContent = [];
    /** @type {Node[]} */
    this._headerActions = [];
    this._onKey = this._onKey.bind(this);
    this._onOverlayClick = this._onOverlayClick.bind(this);
    this._onClose = this._onClose.bind(this);
  }

  connectedCallback() {
    this._captureChildren();
    this._render();
    if (this.hasAttribute('open')) this._onOpen();
  }

  disconnectedCallback() {
    this._cleanup();
  }

  /** @param {string} name */
  attributeChangedCallback(name) {
    if (!this.isConnected) return;
    if (name === 'open') {
      if (this.hasAttribute('open')) this._onOpen();
      else this._cleanup();
    }
    this._render();
  }

  _captureChildren() {
    /** @type {Node[]} */
    const body = [];
    /** @type {Node[]} */
    const footer = [];
    /** @type {Node[]} */
    const headerActions = [];

    while (this.firstChild) {
      const child = this.firstChild;
      this.removeChild(child);
      if (child.nodeType === Node.ELEMENT_NODE) {
        const el = /** @type {Element} */ (child);
        if (el.hasAttribute('data-efs-footer')) {
          footer.push(child);
          continue;
        }
        if (el.hasAttribute('data-efs-header-actions')) {
          headerActions.push(child);
          continue;
        }
      }
      body.push(child);
    }

    this._bodyContent = body;
    this._footerContent = footer;
    this._headerActions = headerActions;
  }

  _onOpen() {
    window.addEventListener('keydown', this._onKey);
    this._previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
  }

  _cleanup() {
    window.removeEventListener('keydown', this._onKey);
    if (this._previousOverflow !== null) {
      document.body.style.overflow = this._previousOverflow;
      this._previousOverflow = null;
    }
  }

  /** @param {KeyboardEvent} e */
  _onKey(e) {
    if (e.key === 'Escape') this._emitClose();
  }

  /** @param {Event} e */
  _onOverlayClick(e) {
    if (e.target === e.currentTarget) this._emitClose();
  }

  _onClose() {
    this._emitClose();
  }

  _emitClose() {
    this.dispatchEvent(new CustomEvent('close', { bubbles: true }));
  }

  _render() {
    if (!this.hasAttribute('open')) {
      this.innerHTML = '';
      return;
    }

    const title = this.getAttribute('title') ?? '';
    const description = this.getAttribute('description') ?? '';
    const eyebrow = this.getAttribute('eyebrow') ?? '';
    const width = this.getAttribute('width') ?? '640px';
    const zIndex = this.getAttribute('z-index') ?? '40';
    const hasFooter = this._footerContent.length > 0;

    this.innerHTML = `
      <div class="fixed inset-0 z-${zIndex} flex" role="dialog" aria-label="${escapeAttr(title)}">
        <div class="flex-1 bg-fg/20 backdrop-blur-[1px]" data-efs-overlay></div>
        <aside class="flex h-full max-w-[100vw] flex-col border-l border-border bg-surface shadow-2xl" style="width: ${escapeAttr(width)}">
          <header class="flex shrink-0 items-start justify-between gap-3 border-b border-border px-4 py-3">
            <div class="min-w-0">
              ${eyebrow ? `<p class="text-[10px] font-semibold uppercase tracking-widest text-fg-dim">${escapeHtml(eyebrow)}</p>` : ''}
              ${title ? `<h2 class="${eyebrow ? 'mt-0.5 ' : ''}text-base font-semibold text-fg">${escapeHtml(title)}</h2>` : ''}
              ${description ? `<p class="mt-1 text-xs text-fg-muted">${escapeHtml(description)}</p>` : ''}
            </div>
            <div class="flex items-center gap-1">
              <div data-efs-header-actions-host></div>
              ${iconButton(CloseIcon('h-4 w-4'), { ariaLabel: 'Fechar', attrs: { 'data-efs-close': true } })}
            </div>
          </header>
          <div class="flex-1 overflow-y-auto" data-efs-body></div>
          ${hasFooter ? `<div class="shrink-0 border-t border-border bg-bg-soft px-4 py-3" data-efs-footer-host></div>` : ''}
        </aside>
      </div>
    `;

    // Re-aloca filhos.
    const bodyHost = this.querySelector('[data-efs-body]');
    if (bodyHost) {
      for (const node of this._bodyContent) bodyHost.appendChild(node);
    }
    const footerHost = this.querySelector('[data-efs-footer-host]');
    if (footerHost) {
      for (const node of this._footerContent) footerHost.appendChild(node);
    }
    const headerActionsHost = this.querySelector('[data-efs-header-actions-host]');
    if (headerActionsHost) {
      for (const node of this._headerActions) headerActionsHost.appendChild(node);
    }

    this.querySelector('[data-efs-overlay]')?.addEventListener('click', this._onOverlayClick);
    this.querySelector('[data-efs-close]')?.addEventListener('click', this._onClose);
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

customElements.define('efs-drawer', EfsDrawer);
