// @ts-check
/**
 * <efs-modal> — substitui mvp/src/ui/Modal.tsx.
 *
 * Light DOM (sem Shadow Root) pra que classes Tailwind globais penetrem.
 * Padrão de filhos: tudo dentro do custom element vai pro corpo;
 * elementos com [data-efs-footer] viram o footer fixo.
 *
 * Atributos:
 *   - open (boolean) — visibilidade
 *   - title (string) — título no header
 *   - description (string) — subtítulo no header
 *   - size (sm|md|lg) — controla max-width (default md)
 *
 * Eventos:
 *   - close (CustomEvent) — disparado quando user clica X, overlay ou ESC
 *
 * Uso:
 *   <efs-modal open title="Confirmar" size="sm">
 *     <p>Tem certeza?</p>
 *     <div data-efs-footer><button>Cancelar</button> <button>OK</button></div>
 *   </efs-modal>
 */

import { CloseIcon } from '../lib/icons.js';

const SIZE_CLASSES = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-2xl',
};

class EfsModal extends HTMLElement {
  static get observedAttributes() {
    return ['open', 'title', 'description', 'size'];
  }

  constructor() {
    super();
    /** @type {string | null} */
    this._previousOverflow = null;
    /** @type {Node[] | null} Filhos originais preservados pra re-inserir no body */
    this._bodyContent = null;
    /** @type {Node[] | null} */
    this._footerContent = null;
    this._onKey = this._onKey.bind(this);
    this._onOverlayClick = this._onOverlayClick.bind(this);
    this._onClose = this._onClose.bind(this);
  }

  connectedCallback() {
    // Captura filhos passados pelo usuário ANTES do innerHTML reset.
    this._captureChildren();
    this._render();
    if (this.hasAttribute('open')) this._onOpen();
  }

  disconnectedCallback() {
    this._onCloseCleanup();
  }

  /**
   * @param {string} name
   */
  attributeChangedCallback(name) {
    if (!this.isConnected) return;
    if (name === 'open') {
      if (this.hasAttribute('open')) this._onOpen();
      else this._onCloseCleanup();
    }
    this._render();
  }

  _captureChildren() {
    /** @type {Node[]} */
    const body = [];
    /** @type {Node[]} */
    const footer = [];
    while (this.firstChild) {
      const child = this.firstChild;
      this.removeChild(child);
      if (child.nodeType === Node.ELEMENT_NODE && /** @type {Element} */ (child).hasAttribute('data-efs-footer')) {
        footer.push(child);
      } else {
        body.push(child);
      }
    }
    this._bodyContent = body;
    this._footerContent = footer.length > 0 ? footer : null;
  }

  _onOpen() {
    window.addEventListener('keydown', this._onKey);
    this._previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
  }

  _onCloseCleanup() {
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
    const size = /** @type {keyof typeof SIZE_CLASSES} */ (this.getAttribute('size') ?? 'md');
    const sizeClass = SIZE_CLASSES[size] ?? SIZE_CLASSES.md;
    const hasHeader = !!(title || description);
    const hasFooter = this._footerContent !== null;

    this.innerHTML = `
      <div class="fixed inset-0 z-50 flex items-center justify-center bg-fg/30 p-6 backdrop-blur-sm"
           role="dialog" aria-modal="true" data-efs-overlay>
        <div class="flex max-h-[calc(100vh-3rem)] w-full flex-col overflow-hidden rounded-2xl border border-border bg-surface shadow-xl ring-1 ring-border/50 ${sizeClass}">
          ${
            hasHeader
              ? `
          <div class="flex shrink-0 items-start justify-between gap-3 border-b border-border px-5 py-4">
            <div class="min-w-0">
              ${title ? `<h2 class="text-base font-semibold text-fg">${title}</h2>` : ''}
              ${description ? `<p class="mt-0.5 text-xs text-fg-muted">${description}</p>` : ''}
            </div>
            <button type="button" aria-label="Fechar" data-efs-close
                    class="inline-flex h-8 w-8 items-center justify-center rounded-md text-fg-muted hover:bg-surface-hover hover:text-fg">
              ${CloseIcon('h-4 w-4')}
            </button>
          </div>`
              : ''
          }
          <div class="flex-1 overflow-y-auto px-5 py-4" data-efs-body></div>
          ${
            hasFooter
              ? `<div class="shrink-0 border-t border-border bg-bg-soft px-5 py-3" data-efs-footer-host></div>`
              : ''
          }
        </div>
      </div>
    `;

    // Re-insere os filhos originais nas posições marcadas.
    const bodyHost = this.querySelector('[data-efs-body]');
    if (bodyHost && this._bodyContent) {
      for (const node of this._bodyContent) bodyHost.appendChild(node);
    }
    const footerHost = this.querySelector('[data-efs-footer-host]');
    if (footerHost && this._footerContent) {
      for (const node of this._footerContent) footerHost.appendChild(node);
    }

    const overlay = this.querySelector('[data-efs-overlay]');
    overlay?.addEventListener('click', this._onOverlayClick);
    const closeBtn = this.querySelector('[data-efs-close]');
    closeBtn?.addEventListener('click', this._onClose);
  }
}

customElements.define('efs-modal', EfsModal);
