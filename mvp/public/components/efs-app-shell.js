// @ts-check
/**
 * <efs-app-shell> — layout padrão pós-onboarding: Sidebar fixa + Header
 * + main slot. Substitui mvp/src/components/Layout.tsx + Sidebar.tsx + Header.tsx.
 *
 * Atributos:
 *   - active (string) — item ativo da sidebar (dashboard|agentes|aprovacoes|
 *     implantacoes|avaliacoes|ferramentas|mcps)
 *
 * Children: tudo que estiver dentro do custom element vira o conteúdo do main.
 *
 * Guard: se não há identidade completa, redireciona pra `/` (Onboarding).
 *
 * Uso em qualquer página interna:
 *   <efs-app-shell active="ferramentas">
 *     <h1>Ferramentas</h1>
 *     ...
 *   </efs-app-shell>
 */

import { getIdentity, subscribeIdentity, clearIdentity, patchIdentity } from '../lib/identity.js';
import { listProjects } from '../lib/projects.js';
import { friendlyError, ApiError } from '../lib/api.js';
import {
  AgentIcon, BoltIcon, ChartIcon, CheckIcon, LogoIcon,
  ServerIcon, SparklesIcon, ToolIcon, ChevronDownIcon, SettingsIcon,
} from '../lib/icons.js';

const NAV_ITEMS = [
  { key: 'dashboard', label: 'Dashboard', href: '/dashboard', icon: ChartIcon },
  { key: 'agentes', label: 'Agentes', href: '/agentes', icon: AgentIcon },
  { key: 'aprovacoes', label: 'Aprovações', href: '/aprovacoes', icon: CheckIcon },
  { key: 'implantacoes', label: 'Implantações', href: '/implantacoes', icon: BoltIcon },
  { key: 'avaliacoes', label: 'Avaliações', href: '/avaliacoes', icon: SparklesIcon },
  { key: 'ferramentas', label: 'Ferramentas', href: '/ferramentas', icon: ToolIcon },
  { key: 'mcps', label: 'MCPs', href: '/mcps', icon: ServerIcon },
];

/** @param {string} name */
function initialsFromName(name) {
  return name
    .split(/\s+/)
    .map((p) => p[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase() || '?';
}

class EfsAppShell extends HTMLElement {
  constructor() {
    super();
    /** @type {Node[]} */
    this._mainContent = [];
    /** @type {(() => void) | null} */
    this._unsubIdentity = null;
    this._onSettingsClick = this._onSettingsClick.bind(this);
  }

  connectedCallback() {
    // Guard: redireciona se não tem identidade completa.
    const identity = getIdentity();
    if (!identity?.account || !identity.projectId) {
      window.location.replace('/');
      return;
    }

    // Captura children (conteúdo do main) ANTES do innerHTML reset.
    while (this.firstChild) {
      this._mainContent.push(this.firstChild);
      this.removeChild(this.firstChild);
    }

    this._render();

    // Re-renderiza header quando identity muda (ex: troca de projeto pelo modal).
    this._unsubIdentity = subscribeIdentity(() => this._renderHeader());
  }

  disconnectedCallback() {
    this._unsubIdentity?.();
  }

  _render() {
    const active = this.getAttribute('active') ?? '';

    this.innerHTML = `
      <div class="flex h-screen overflow-hidden">
        ${this._sidebarHtml(active)}
        <div class="flex flex-1 flex-col overflow-hidden">
          <div data-efs-header></div>
          <main class="flex-1 overflow-y-auto px-8 py-8" data-efs-main></main>
        </div>
      </div>
    `;

    // Realoca o conteúdo capturado no <main>.
    const mainEl = this.querySelector('[data-efs-main]');
    if (mainEl) {
      for (const node of this._mainContent) mainEl.appendChild(node);
    }

    this._renderHeader();
  }

  /** @param {string} active */
  _sidebarHtml(active) {
    const items = NAV_ITEMS.map((item) => {
      const isActive = item.key === active;
      const stateClasses = isActive
        ? 'bg-accent-subtle text-accent before:bg-accent'
        : 'text-fg-muted before:bg-transparent hover:bg-surface-hover hover:text-fg';
      return `
        <a href="${item.href}"
           class="relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition before:absolute before:inset-y-1.5 before:left-0 before:w-0.5 before:rounded-r before:transition ${stateClasses}">
          ${item.icon('h-5 w-5')}
          <span>${item.label}</span>
        </a>
      `;
    }).join('');

    return `
      <aside class="flex w-60 shrink-0 flex-col border-r border-border bg-bg-soft">
        <div class="flex items-center gap-2 px-5 py-5">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-accent-subtle text-accent">
            ${LogoIcon('h-4 w-4')}
          </div>
          <div class="leading-tight">
            <div class="text-sm font-semibold tracking-tight text-fg">AI Hub</div>
            <div class="text-[10px] uppercase tracking-widest text-fg-dim">Governance Platform</div>
          </div>
        </div>
        <nav class="flex-1 px-3 py-2">
          ${items}
        </nav>
      </aside>
    `;
  }

  _renderHeader() {
    const headerHost = this.querySelector('[data-efs-header]');
    if (!headerHost) return;

    const identity = getIdentity();
    const initials = identity?.name ? initialsFromName(identity.name) : '?';
    const projectLabel = identity?.projectName?.trim() ? identity.projectName : 'Selecionar projeto';

    headerHost.innerHTML = `
      <header class="flex items-center justify-between border-b border-border bg-bg-soft px-8 py-4">
        <div class="flex items-center gap-3">
          <div class="text-[11px] uppercase tracking-widest text-fg-dim">Projeto</div>
          <button type="button" data-efs-open-settings
                  class="flex items-center gap-2 rounded-lg border border-border bg-surface px-3 py-1.5 text-sm text-fg transition hover:bg-surface-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30">
            <span class="font-medium">${escapeHtml(projectLabel)}</span>
            ${ChevronDownIcon('h-3.5 w-3.5 text-fg-dim')}
          </button>
        </div>

        <div class="flex items-center gap-3">
          <efs-theme-toggle></efs-theme-toggle>
          <button type="button" aria-label="Configurações" data-efs-open-settings
                  class="inline-flex h-9 w-9 items-center justify-center rounded-lg text-fg-muted hover:bg-surface-hover hover:text-fg transition-colors">
            ${SettingsIcon('h-4 w-4')}
          </button>
          <div class="flex items-center gap-2.5 px-1">
            <div class="flex h-8 w-8 items-center justify-center rounded-full bg-accent-subtle text-[11px] font-semibold text-accent">
              ${escapeHtml(initials)}
            </div>
            <div class="leading-tight">
              <div class="text-xs font-medium text-fg">${escapeHtml(identity?.name ?? '')}</div>
              <div class="text-[10px] text-fg-dim">conta ${escapeHtml(identity?.account ?? '')}</div>
            </div>
          </div>
        </div>
      </header>
    `;

    // Wire dos botões que abrem o modal de configurações.
    headerHost.querySelectorAll('[data-efs-open-settings]').forEach((btn) => {
      btn.addEventListener('click', this._onSettingsClick);
    });
  }

  _onSettingsClick() {
    let modal = /** @type {HTMLElement | null} */ (document.querySelector('efs-project-modal'));
    if (!modal) {
      modal = document.createElement('efs-project-modal');
      document.body.appendChild(modal);
    }
    modal.setAttribute('open', '');
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

customElements.define('efs-app-shell', EfsAppShell);
