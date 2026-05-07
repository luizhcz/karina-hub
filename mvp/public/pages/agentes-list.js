// @ts-check
/**
 * Página /agentes — tabs (Rascunhos | Publicados) + busca + flash messages
 * + "Novo agente" modal de mode (basic/advanced). Substitui
 * mvp/src/routes/AgentsList.tsx.
 *
 * Simplificações Fase 4 (vs React 1104 LOC):
 * - Approval history modal (clicar agente publicado abre histórico): omitido
 *   — raramente usado no fluxo principal, fica pra pós-Fase-5 se priorizado.
 * - Enable/disable agent modal: idem.
 * - Templates de agentes (CatalogPicker): omitido — modal de mode mostra
 *   só o toggle basic/advanced. Catálogo entra na Fase 4 PR 4c se necessário.
 *
 * Flash messages: lê de sessionStorage (chave 'efs-flash') quando o
 * AgentEditor redireciona pra cá após submit. Substitui location.state do
 * React Router.
 */

import { listAgentDrafts } from '../lib/agent-drafts.js';
import { listAgents, createEditDraft } from '../lib/agents.js';
import { isInCurrentProject } from '../lib/project-scope.js';
import { ApiError, friendlyError } from '../lib/api.js';
import { getIdentity } from '../lib/identity.js';
import { AgentIcon, CheckIcon, CloseIcon, PlusIcon, SearchIcon } from '../lib/icons.js';
import { badge, button, errorMessage } from '../lib/ui.js';

/** @typedef {import('../lib/agent-drafts.js').AgentDraft} AgentDraft */
/** @typedef {import('../lib/agents.js').Agent} Agent */

const root = /** @type {HTMLElement} */ (document.getElementById('agentes-page'));

/** @type {'drafts' | 'published'} */
let activeTab = new URLSearchParams(window.location.search).get('tab') === 'published' ? 'published' : 'drafts';

/** @type {AgentDraft[]} */
let drafts = [];
let draftsLoading = true;
/** @type {string | null} */
let draftsError = null;

/** @type {Agent[]} */
let agents = [];
let agentsLoading = true;
/** @type {string | null} */
let agentsError = null;

let search = '';
let onlyMine = false;
let modeModalOpen = false;
/** @type {string | null} */
let forkError = null;
/** @type {string | null} */
let forkingId = null;

const identity = getIdentity();
const myAccount = identity?.account ?? null;

/** @type {{ tone: 'success' | 'accent', title: string, body?: string } | null} */
let flash = readFlashFromSession();

renderShell();
loadAll();

function readFlashFromSession() {
  try {
    const raw = sessionStorage.getItem('efs-flash');
    if (!raw) return null;
    sessionStorage.removeItem('efs-flash');
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

async function loadAll() {
  draftsLoading = true;
  agentsLoading = true;
  draftsError = null;
  agentsError = null;
  renderBody();

  try {
    drafts = await listAgentDrafts();
  } catch (err) {
    draftsError = friendlyError(err, 'Não foi possível carregar os rascunhos.');
  } finally {
    draftsLoading = false;
    renderBody();
  }

  try {
    agents = await listAgents();
  } catch (err) {
    agentsError = friendlyError(err, 'Não foi possível carregar os agentes publicados.');
  } finally {
    agentsLoading = false;
    renderBody();
  }
}

function renderShell() {
  root.innerHTML = `
    <div id="flash-container"></div>

    <div class="mb-8 flex items-end justify-between">
      <div>
        <h1 class="text-[28px] font-semibold tracking-tight text-fg">Agentes</h1>
        <p class="mt-2 text-sm text-fg-muted">Gerencie seus rascunhos e visualize os agentes publicados após aprovação.</p>
      </div>
      <div id="header-action"></div>
    </div>

    <div class="mb-6 flex items-center gap-1 border-b border-border" id="tabs"></div>

    <div class="mb-6 flex flex-wrap items-center gap-3">
      <div class="relative max-w-md flex-1">
        <span class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">${SearchIcon('h-4 w-4')}</span>
        <input id="search-input" type="text" value="${escapeAttr(search)}"
               placeholder="Buscar por nome ou descrição…"
               class="w-full rounded-lg border border-border bg-surface pl-10 pr-3 py-2 text-sm text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
      </div>
      <div id="mine-toggle"></div>
    </div>

    <div id="agentes-body"></div>
  `;

  renderFlash();
  renderHeaderAction();
  renderTabs();
  renderMineToggle();

  /** @type {HTMLInputElement | null} */
  const searchInput = root.querySelector('#search-input');
  searchInput?.addEventListener('input', () => {
    search = searchInput.value;
    renderBody();
  });

  renderBody();
  renderModeModal();
}

function renderFlash() {
  const host = /** @type {HTMLElement} */ (root.querySelector('#flash-container'));
  if (!host) return;
  if (!flash) {
    host.innerHTML = '';
    return;
  }
  const tone = flash.tone === 'success'
    ? 'border-success/40 bg-success/10 text-success'
    : 'border-accent/40 bg-accent-subtle text-accent';
  host.innerHTML = `
    <div class="mb-6 flex items-start justify-between gap-3 rounded-lg border ${tone} px-4 py-3 text-sm">
      <div>
        <p class="font-semibold">${escapeHtml(flash.title)}</p>
        ${flash.body ? `<p class="mt-1 text-xs text-fg-muted">${escapeHtml(flash.body)}</p>` : ''}
      </div>
      <button type="button" data-flash-close class="rounded-md p-1 text-fg-muted hover:bg-surface-hover hover:text-fg">${CloseIcon('h-4 w-4')}</button>
    </div>
  `;
  host.querySelector('[data-flash-close]')?.addEventListener('click', () => {
    flash = null;
    renderFlash();
  });
}

function renderHeaderAction() {
  const host = /** @type {HTMLElement} */ (root.querySelector('#header-action'));
  if (!host) return;
  if (activeTab === 'drafts') {
    host.innerHTML = button({
      label: 'Novo agente',
      leftIcon: PlusIcon('h-4 w-4'),
      attrs: { 'data-new-agent': true },
    });
    host.querySelector('[data-new-agent]')?.addEventListener('click', () => {
      modeModalOpen = true;
      renderModeModal();
    });
  } else {
    host.innerHTML = '';
  }
}

function renderTabs() {
  const host = /** @type {HTMLElement} */ (root.querySelector('#tabs'));
  if (!host) return;

  const ownDrafts = drafts.filter(isInCurrentProject);
  const ownAgents = agents.filter(isInCurrentProject);

  /** @param {'drafts' | 'published'} key @param {string} label @param {number | null} count */
  const tab = (key, label, count) => {
    const isActive = activeTab === key;
    const stateClasses = isActive ? 'text-fg' : 'text-fg-muted hover:text-fg';
    return `
      <button type="button" data-tab="${key}"
              class="relative flex items-center gap-2 px-4 py-2.5 text-sm font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30 ${stateClasses}">
        ${escapeHtml(label)}
        ${count !== null ? `<span class="rounded-full bg-bg-soft px-1.5 text-[10px] font-semibold text-fg-dim">${count}</span>` : ''}
        ${isActive ? '<span class="absolute inset-x-0 bottom-0 h-0.5 bg-accent" aria-hidden="true"></span>' : ''}
      </button>
    `;
  };

  host.innerHTML = [
    tab('drafts', 'Rascunhos', draftsLoading ? null : ownDrafts.length),
    tab('published', 'Publicados', agentsLoading ? null : ownAgents.length),
  ].join('');

  host.querySelectorAll('[data-tab]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const next = /** @type {'drafts' | 'published'} */ (btn.getAttribute('data-tab'));
      if (next === activeTab) return;
      activeTab = next;
      const url = new URL(window.location.href);
      if (next === 'published') url.searchParams.set('tab', 'published');
      else url.searchParams.delete('tab');
      window.history.replaceState({}, '', url.toString());
      renderShell();
    });
  });
}

function renderMineToggle() {
  const host = /** @type {HTMLElement} */ (root.querySelector('#mine-toggle'));
  if (!host) return;
  if (activeTab !== 'drafts' || !myAccount) {
    host.innerHTML = '';
    return;
  }

  const ownDrafts = drafts.filter(isInCurrentProject);
  const myCount = ownDrafts.filter((d) => d.createdBy === myAccount).length;

  const stateClasses = onlyMine
    ? 'border-accent bg-accent-subtle text-accent'
    : 'border-border bg-surface text-fg-muted hover:text-fg';

  host.innerHTML = `
    <button type="button" data-toggle-mine
            class="inline-flex h-9 items-center gap-2 rounded-lg border px-3 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30 ${stateClasses}"
            aria-pressed="${onlyMine}">
      ${onlyMine ? CheckIcon('h-3.5 w-3.5') : ''}
      <span>Meus rascunhos</span>
      <span class="rounded-full bg-bg-soft px-1.5 text-[10px] font-semibold text-fg-dim">${myCount}</span>
    </button>
  `;
  host.querySelector('[data-toggle-mine]')?.addEventListener('click', () => {
    onlyMine = !onlyMine;
    renderBody();
    renderMineToggle();
  });
}

function renderBody() {
  const body = /** @type {HTMLElement} */ (root.querySelector('#agentes-body'));
  if (!body) return;
  body.innerHTML = activeTab === 'drafts' ? draftsBodyHtml() : agentsBodyHtml();
  wireBodyActions();
  // Re-render contadores nos tabs (mudaram com dados).
  renderTabs();
}

function draftsBodyHtml() {
  if (draftsLoading) {
    return `<efs-card><div class="flex items-center justify-center py-12"><efs-spinner class="inline-flex h-6 w-6 text-fg-muted"></efs-spinner></div></efs-card>`;
  }
  if (draftsError) return errorMessage(draftsError);

  const ownDrafts = drafts.filter(isInCurrentProject);
  const trimmed = search.trim().toLowerCase();
  const filtered = ownDrafts.filter((d) => {
    if (onlyMine && myAccount && d.createdBy !== myAccount) return false;
    if (!trimmed) return true;
    const name = (d.name || /** @type {string} */ (d.payload?.name) || '').toLowerCase();
    const desc = (/** @type {string} */ (d.payload?.description) ?? '').toLowerCase();
    return name.includes(trimmed) || desc.includes(trimmed);
  });

  if (filtered.length === 0) {
    if (ownDrafts.length === 0) {
      return `
        <efs-card>
          <div class="text-center">
            <h3 class="text-sm font-semibold text-fg">Nenhum rascunho ainda</h3>
            <p class="mt-1 text-xs text-fg-muted">Clique em <strong>Novo agente</strong> pra criar seu primeiro rascunho.</p>
          </div>
        </efs-card>
      `;
    }
    return `<efs-card><p class="text-center text-sm text-fg-muted">Nada bate com a busca.</p></efs-card>`;
  }

  return `
    <div class="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
      ${filtered.map(draftCardHtml).join('')}
    </div>
    ${forkError ? `<div class="mt-4">${errorMessage(forkError)}</div>` : ''}
  `;
}

/** @param {AgentDraft} d */
function draftCardHtml(d) {
  const name = d.name || /** @type {string} */ (d.payload?.name) || 'Rascunho sem nome';
  const description = /** @type {string} */ (d.payload?.description) ?? '';

  /** @type {Record<string, { tone: 'neutral' | 'accent' | 'warning', label: string, stripe: string }>} */
  const META = {
    Draft:           { tone: 'neutral', label: 'Rascunho', stripe: 'before:bg-fg-dim/30' },
    PendingApproval: { tone: 'accent',  label: 'Aguardando aprovação', stripe: 'before:bg-accent' },
    Rejected:        { tone: 'warning', label: 'Rejeitado', stripe: 'before:bg-warning' },
  };
  const meta = META[d.status];

  return `
    <efs-card interactive padded="false">
      <div data-edit-draft="${escapeAttr(d.id)}" role="button" tabindex="0"
           class="group relative flex min-h-[180px] cursor-pointer flex-col gap-3 overflow-hidden p-5 before:absolute before:inset-y-0 before:left-0 before:w-1 ${meta.stripe}">
        <div class="flex items-start justify-between gap-3">
          <div class="flex min-w-0 items-center gap-3">
            <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
              ${AgentIcon('h-5 w-5')}
            </div>
            <h3 class="min-w-0 truncate text-sm font-semibold text-fg">${escapeHtml(name)}</h3>
          </div>
          ${badge(meta.label, { tone: meta.tone })}
        </div>
        <p class="line-clamp-3 text-xs text-fg-muted">
          ${description ? escapeHtml(description) : '<span class="italic text-fg-dim">sem descrição</span>'}
        </p>
        <div class="mt-auto flex items-center justify-between text-[11px] text-fg-dim">
          ${d.isEditDraft ? badge('edição', { tone: 'neutral' }) : '<span></span>'}
          <span>atualizado ${escapeHtml(formatRelative(d.updatedAt))}</span>
        </div>
      </div>
    </efs-card>
  `;
}

function agentsBodyHtml() {
  if (agentsLoading) {
    return `<efs-card><div class="flex items-center justify-center py-12"><efs-spinner class="inline-flex h-6 w-6 text-fg-muted"></efs-spinner></div></efs-card>`;
  }
  if (agentsError) return errorMessage(agentsError);

  const ownAgents = agents.filter(isInCurrentProject);
  const trimmed = search.trim().toLowerCase();
  const filtered = trimmed
    ? ownAgents.filter((a) => (a.name ?? '').toLowerCase().includes(trimmed) || (a.description ?? '').toLowerCase().includes(trimmed))
    : ownAgents;

  if (filtered.length === 0) {
    if (ownAgents.length === 0) {
      return `
        <efs-card>
          <div class="text-center">
            <h3 class="text-sm font-semibold text-fg">Nenhum agente publicado</h3>
            <p class="mt-1 text-xs text-fg-muted">Quando um rascunho for aprovado, o agente publicado aparece aqui.</p>
          </div>
        </efs-card>
      `;
    }
    return `<efs-card><p class="text-center text-sm text-fg-muted">Nada bate com a busca.</p></efs-card>`;
  }

  return `
    <div class="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
      ${filtered.map(agentCardHtml).join('')}
    </div>
  `;
}

/** @param {Agent} a */
function agentCardHtml(a) {
  const enabled = a.enabled !== false;
  const stripe = enabled ? 'before:bg-success' : 'before:bg-fg-dim/30';
  const isForking = forkingId === a.id;
  return `
    <efs-card interactive padded="false">
      <div class="group relative flex min-h-[180px] flex-col gap-3 overflow-hidden p-5 before:absolute before:inset-y-0 before:left-0 before:w-1 ${stripe}">
        <div class="flex items-start justify-between gap-3">
          <div class="flex min-w-0 items-center gap-3">
            <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
              ${AgentIcon('h-5 w-5')}
            </div>
            <h3 class="min-w-0 truncate text-sm font-semibold text-fg">${escapeHtml(a.name ?? '')}</h3>
          </div>
          ${enabled ? badge('Ativo', { tone: 'success' }) : badge('Desabilitado', { tone: 'neutral' })}
        </div>
        <p class="line-clamp-3 text-xs text-fg-muted">
          ${a.description ? escapeHtml(a.description) : '<span class="italic text-fg-dim">sem descrição</span>'}
        </p>
        <div class="flex flex-wrap items-center gap-2 text-[11px]">
          <a href="/agentes/${encodeURIComponent(a.id)}/sandbox" class="rounded-md border border-border bg-surface px-2 py-1 text-fg-muted hover:bg-surface-hover hover:text-fg">Sandbox →</a>
          <a href="/agentes/${encodeURIComponent(a.id)}/versoes" class="rounded-md border border-border bg-surface px-2 py-1 text-fg-muted hover:bg-surface-hover hover:text-fg">Versões</a>
          <a href="/agentes/${encodeURIComponent(a.id)}/implantar" class="rounded-md border border-border bg-surface px-2 py-1 text-fg-muted hover:bg-surface-hover hover:text-fg">Implantar</a>
          <button type="button" data-fork-agent="${escapeAttr(a.id)}" ${isForking ? 'disabled' : ''}
                  class="ml-auto rounded-md border border-accent bg-accent px-2 py-1 text-accent-contrast hover:bg-accent-soft disabled:opacity-50">
            ${isForking ? 'Abrindo…' : 'Editar'}
          </button>
        </div>
      </div>
    </efs-card>
  `;
}

function wireBodyActions() {
  // Cards de drafts → abre editor
  root.querySelectorAll('[data-edit-draft]').forEach((card) => {
    const id = card.getAttribute('data-edit-draft');
    if (!id) return;
    /** @param {Event | KeyboardEvent} e */
    const open = (e) => {
      if ('key' in e && e.key !== 'Enter' && e.key !== ' ') return;
      e.preventDefault();
      window.location.assign(`/agentes/${encodeURIComponent(id)}`);
    };
    card.addEventListener('click', open);
    card.addEventListener('keydown', open);
  });

  // Botão "Editar" em agente publicado → fork edit-draft
  root.querySelectorAll('[data-fork-agent]').forEach((btn) => {
    btn.addEventListener('click', async (e) => {
      e.stopPropagation();
      const id = btn.getAttribute('data-fork-agent');
      if (!id) return;
      forkingId = id;
      forkError = null;
      renderBody();
      try {
        const draft = await createEditDraft(id);
        window.location.assign(`/agentes/${encodeURIComponent(draft.id)}`);
      } catch (err) {
        forkError = err instanceof ApiError && err.status === 409
          ? 'Já existe um rascunho de edição em aberto para este agente. Procure-o na aba Rascunhos.'
          : friendlyError(err, 'Não foi possível abrir o agente para edição.');
        forkingId = null;
        renderBody();
      }
    });
  });
}

// ── Modal "Novo agente" ──────────────────────────────────────────────────

let modeModalEl = /** @type {HTMLElement | null} */ (null);

function renderModeModal() {
  if (modeModalOpen) {
    if (!modeModalEl) {
      modeModalEl = document.createElement('efs-modal');
      modeModalEl.setAttribute('size', 'md');
      modeModalEl.setAttribute('title', 'Novo agente');
      modeModalEl.setAttribute('description', 'Escolha o modo de criação. O modo Avançado expõe configurações de Tools, MCPs e schemas de input/output.');
      modeModalEl.addEventListener('close', () => {
        modeModalOpen = false;
        modeModalEl?.removeAttribute('open');
      });
      document.body.appendChild(modeModalEl);
    }
    modeModalEl.innerHTML = `
      <div class="space-y-3">
        <button type="button" data-mode="basic"
                class="flex w-full items-start gap-3 rounded-lg border border-border bg-surface p-4 text-left hover:border-accent hover:bg-accent-subtle/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40">
          <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">${AgentIcon('h-5 w-5')}</div>
          <div class="min-w-0">
            <p class="text-sm font-semibold text-fg">Básico</p>
            <p class="mt-1 text-xs text-fg-muted">Perfil + modelo + descrição. Recomendado pra agentes de chat livre.</p>
          </div>
        </button>
        <button type="button" data-mode="advanced"
                class="flex w-full items-start gap-3 rounded-lg border border-border bg-surface p-4 text-left hover:border-accent hover:bg-accent-subtle/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40">
          <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">${AgentIcon('h-5 w-5')}</div>
          <div class="min-w-0">
            <p class="text-sm font-semibold text-fg">Avançado</p>
            <p class="mt-1 text-xs text-fg-muted">Inclui Tools, MCPs e schemas estruturados de input/output.</p>
          </div>
        </button>
      </div>
    `;
    modeModalEl.setAttribute('open', '');
    modeModalEl.querySelectorAll('[data-mode]').forEach((btn) => {
      btn.addEventListener('click', () => {
        const mode = btn.getAttribute('data-mode');
        modeModalOpen = false;
        modeModalEl?.removeAttribute('open');
        window.location.assign(`/agentes/novo?mode=${mode}`);
      });
    });
  } else if (modeModalEl) {
    modeModalEl.removeAttribute('open');
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────

/** @param {string} iso */
function formatRelative(iso) {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const diff = Date.now() - date.getTime();
  const min = Math.round(diff / 60_000);
  if (min < 1) return 'agora';
  if (min < 60) return `há ${min} min`;
  const h = Math.round(min / 60);
  if (h < 24) return `há ${h} h`;
  const d = Math.round(h / 24);
  if (d < 7) return `há ${d} d`;
  return date.toLocaleDateString('pt-BR');
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
