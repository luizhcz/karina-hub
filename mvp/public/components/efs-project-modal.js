// @ts-check
/**
 * <efs-project-modal> — modal de configurações: troca de projeto + sign-out.
 * Substitui mvp/src/components/ProjectSelectorModal.tsx.
 *
 * Auto-injetado pelo efs-app-shell quando user clica no header. Abre via
 * setAttribute('open', '').
 *
 * Usa <efs-modal> internamente — mesma keyboard handling + scroll lock.
 */

import { listProjects } from '../lib/projects.js';
import { ApiError, friendlyError } from '../lib/api.js';
import { clearIdentity, getIdentity, patchIdentity } from '../lib/identity.js';
import { CheckIcon } from '../lib/icons.js';

class EfsProjectModal extends HTMLElement {
  static get observedAttributes() {
    return ['open'];
  }

  constructor() {
    super();
    /** @type {Array<{ id: string, name: string, description?: string | null }>} */
    this._projects = [];
    this._loading = false;
    /** @type {string | null} */
    this._error = null;
    this._onModalClose = this._onModalClose.bind(this);
  }

  connectedCallback() {
    this._render();
  }

  /** @param {string} name */
  attributeChangedCallback(name) {
    if (name === 'open' && this.hasAttribute('open')) {
      this._loadProjects();
    }
    this._render();
  }

  async _loadProjects() {
    this._loading = true;
    this._error = null;
    this._render();
    try {
      this._projects = await listProjects();
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        this._error = 'Não foi possível carregar os projetos disponíveis.';
      } else {
        this._error = friendlyError(err, 'Não foi possível carregar os projetos.');
      }
    } finally {
      this._loading = false;
      this._render();
    }
  }

  /** @param {{ id: string, name: string }} project */
  _onPick(project) {
    patchIdentity({ projectId: project.id, projectName: project.name });
    this._close();
  }

  _onSignOut() {
    clearIdentity();
    this._close();
    // Onboarding é o destino lógico — força reload pra garantir estado limpo.
    window.location.assign('/');
  }

  _onModalClose() {
    this._close();
  }

  _close() {
    this.removeAttribute('open');
  }

  _render() {
    if (!this.hasAttribute('open')) {
      this.innerHTML = '';
      return;
    }

    const current = getIdentity();
    const items = this._loading
      ? `
        <div class="flex items-center justify-center py-6 text-fg-muted">
          <efs-spinner class="inline-flex h-5 w-5"></efs-spinner>
        </div>`
      : this._error
        ? `<p class="rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-xs text-danger">${escapeHtml(this._error)}</p>`
        : this._projects.length === 0
          ? `<div class="rounded-lg border border-dashed border-border px-4 py-6 text-center text-sm text-fg-muted">Nenhum projeto disponível.</div>`
          : `
            <div class="max-h-72 space-y-2 overflow-y-auto pr-1">
              ${this._projects.map((p) => {
                const active = p.id === current?.projectId;
                const stateClasses = active
                  ? 'border-accent/60 bg-accent-subtle'
                  : 'border-border bg-surface hover:border-border-strong hover:bg-surface-hover';
                return `
                  <button type="button" data-efs-project-id="${escapeAttr(p.id)}"
                          class="flex w-full items-center justify-between rounded-lg border px-3 py-3 text-left transition ${stateClasses}">
                    <div class="min-w-0">
                      <div class="text-sm font-medium text-fg">${escapeHtml(p.name)}</div>
                      ${p.description ? `<div class="mt-0.5 truncate text-[11px] text-fg-muted">${escapeHtml(p.description)}</div>` : ''}
                    </div>
                    ${active ? `
                      <span class="inline-flex shrink-0 items-center gap-1 rounded-full border border-accent/40 bg-accent-subtle px-2 py-0.5 text-[10px] font-medium uppercase tracking-wide text-accent">
                        ${CheckIcon('h-3 w-3')}
                        Atual
                      </span>` : ''}
                  </button>
                `;
              }).join('')}
            </div>
          `;

    this.innerHTML = `
      <efs-modal open title="Configurações" description="Escolha o projeto em que vai trabalhar." size="md">
        ${items}
        <div data-efs-footer class="flex items-center justify-between text-[11px] text-fg-dim">
          <div>
            <span class="block">${escapeHtml(current?.name ?? '')}</span>
            <span class="block font-mono">conta ${escapeHtml(current?.account ?? '')}</span>
          </div>
          <button type="button" data-efs-sign-out
                  class="rounded-md px-2 py-1 text-fg-muted hover:bg-surface-hover hover:text-fg">
            Trocar identidade
          </button>
        </div>
      </efs-modal>
    `;

    const modal = this.querySelector('efs-modal');
    modal?.addEventListener('close', this._onModalClose);

    this.querySelectorAll('[data-efs-project-id]').forEach((btn) => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-efs-project-id');
        const project = this._projects.find((p) => p.id === id);
        if (project) this._onPick(project);
      });
    });

    this.querySelector('[data-efs-sign-out]')?.addEventListener('click', () => this._onSignOut());
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
function escapeAttr(v) {
  return escapeHtml(v);
}

customElements.define('efs-project-modal', EfsProjectModal);
