// @ts-check
/**
 * Página /implantacoes — lista deployments (workflows criados pela tela de
 * AgentDeploy) + último eval run por agente. Substitui
 * mvp/src/routes/Implantacoes.tsx.
 *
 * Pattern de carga em 2 fases:
 *  1. listWorkflows + filtra isAgentDeployment + isInCurrentProject
 *  2. Em paralelo (Promise.allSettled), listEvalRunsByAgent(agentId, 1) por agente
 *     pra alimentar o badge de status de eval.
 */

import { listWorkflows, isAgentDeployment, deployedAgentId } from '../lib/workflows.js';
import { listEvalRunsByAgent } from '../lib/profile-evaluation.js';
import { listAgents } from '../lib/agents.js';
import { isInCurrentProject } from '../lib/project-scope.js';
import { friendlyError } from '../lib/api.js';
import { AgentIcon, BoltIcon, PlusIcon, SearchIcon } from '../lib/icons.js';

/** @typedef {import('../lib/workflows.js').Workflow} Workflow */
/** @typedef {import('../lib/profile-evaluation.js').EvalRunSummary} EvalRunSummary */
/** @typedef {import('../lib/agents.js').Agent} Agent */

const root = /** @type {HTMLElement} */ (document.getElementById('implantacoes-page'));

/** @type {Workflow[]} */
let workflows = [];
/** @type {Map<string, EvalRunSummary>} */
let lastEvalByAgent = new Map();
let loading = true;
/** @type {string | null} */
let error = null;
let search = '';

renderShell();
loadDeployments();

async function loadDeployments() {
  loading = true;
  error = null;
  renderBody();
  try {
    const all = await listWorkflows();
    workflows = all.filter(isAgentDeployment).filter(isInCurrentProject);

    // Best-effort: carrega último eval run por agente em paralelo.
    const agentIds = workflows.map(deployedAgentId).filter(Boolean);
    Promise.allSettled(
      agentIds.map(async (aid) => {
        const runs = await listEvalRunsByAgent(/** @type {string} */ (aid), 1);
        return { aid: /** @type {string} */ (aid), run: runs[0] };
      }),
    ).then((results) => {
      const next = new Map();
      for (const r of results) {
        if (r.status === 'fulfilled' && r.value.run) {
          next.set(r.value.aid, r.value.run);
        }
      }
      lastEvalByAgent = next;
      renderBody();
    });
  } catch (err) {
    error = friendlyError(err, 'Não foi possível carregar as implantações.');
    workflows = [];
  } finally {
    loading = false;
    renderBody();
  }
}

function renderShell() {
  root.innerHTML = `
    <div class="mb-8 flex items-end justify-between">
      <div>
        <h1 class="text-[28px] font-semibold tracking-tight text-fg">Implantações</h1>
        <p class="mt-2 text-sm text-fg-muted">
          Cada implantação cria um workflow Graph que expõe um agente publicado para consumo via API.
        </p>
      </div>
      <button id="open-picker" type="button"
              class="inline-flex items-center gap-2 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-contrast hover:bg-accent-soft">
        ${PlusIcon('h-4 w-4')}
        <span>Nova implantação</span>
      </button>
    </div>

    <div class="mb-6 max-w-md">
      <div class="relative">
        <span class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">
          ${SearchIcon('h-4 w-4')}
        </span>
        <input id="search-input" type="text"
               placeholder="Buscar por nome, descrição ou id…"
               class="w-full rounded-lg border border-border bg-surface pl-10 pr-3 py-2 text-sm text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
      </div>
    </div>

    <div id="deployments-body"></div>
  `;

  /** @type {HTMLInputElement | null} */
  const searchInput = root.querySelector('#search-input');
  searchInput?.addEventListener('input', () => {
    search = searchInput.value;
    renderBody();
  });

  root.querySelector('#open-picker')?.addEventListener('click', openPicker);
}

function renderBody() {
  const body = /** @type {HTMLElement} */ (root.querySelector('#deployments-body'));
  if (!body) return;

  if (loading) {
    body.innerHTML = `
      <efs-card>
        <div class="flex items-center justify-center py-12">
          <efs-spinner class="inline-flex h-6 w-6 text-fg-muted"></efs-spinner>
        </div>
      </efs-card>
    `;
    return;
  }

  if (error) {
    body.innerHTML = `<p class="rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">${escapeHtml(error)}</p>`;
    return;
  }

  if (workflows.length === 0) {
    body.innerHTML = `
      <efs-card>
        <p class="text-center text-sm text-fg-muted">
          Nenhuma implantação ainda. Clique em <strong>Nova implantação</strong> para colocar um agente publicado em produção.
        </p>
      </efs-card>
    `;
    return;
  }

  const filtered = filterDeployments(workflows, search);
  if (filtered.length === 0) {
    body.innerHTML = `
      <efs-card>
        <p class="text-center text-sm text-fg-muted">Nada bate com a busca. Tente ajustar o termo.</p>
      </efs-card>
    `;
    return;
  }

  body.innerHTML = `
    <div class="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
      ${filtered.map(deploymentCardHtml).join('')}
    </div>
  `;

  body.querySelectorAll('[data-agent-id]').forEach((card) => {
    const id = card.getAttribute('data-agent-id');
    if (!id) return;
    /** @param {Event | KeyboardEvent} e */
    const goToDeploy = (e) => {
      if ('key' in e && e.key !== 'Enter' && e.key !== ' ') return;
      e.preventDefault();
      window.location.assign(`/agentes/${encodeURIComponent(id)}/implantar`);
    };
    card.addEventListener('click', goToDeploy);
    card.addEventListener('keydown', goToDeploy);
  });
}

/**
 * @param {Workflow[]} list
 * @param {string} q
 */
function filterDeployments(list, q) {
  const trimmed = q.trim().toLowerCase();
  if (!trimmed) return list;
  return list.filter((w) =>
    (w.name ?? '').toLowerCase().includes(trimmed)
    || (w.description ?? '').toLowerCase().includes(trimmed)
    || w.id.toLowerCase().includes(trimmed),
  );
}

/** @param {Workflow} w */
function deploymentCardHtml(w) {
  const aid = deployedAgentId(w);
  const evalRun = aid ? lastEvalByAgent.get(aid) ?? null : null;
  const evalBadge = evalRun ? evalBadgeHtml(evalRun) : '';
  const canClick = !!aid;

  return `
    <efs-card interactive padded="false">
      <div ${canClick ? `data-agent-id="${escapeAttr(aid)}"` : ''}
           ${canClick ? 'role="button" tabindex="0"' : ''}
           class="group relative flex min-h-[160px] ${canClick ? 'cursor-pointer' : ''} flex-col gap-3 overflow-hidden p-5 before:absolute before:inset-y-0 before:left-0 before:w-1 before:bg-success">
        <div class="flex items-start justify-between gap-3">
          <div class="flex min-w-0 items-center gap-3">
            <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-success/10 text-success">
              ${BoltIcon('h-5 w-5')}
            </div>
            <div class="min-w-0">
              <h3 class="truncate text-sm font-semibold text-fg">${escapeHtml(w.name)}</h3>
              <p class="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">${escapeHtml(w.id)}</p>
            </div>
          </div>
          <div class="flex flex-col items-end gap-1">
            <span class="inline-flex items-center rounded-full border border-success/40 bg-success/10 px-2 py-0.5 text-[10px] font-medium text-success">Implantado</span>
            ${evalBadge}
          </div>
        </div>
        ${w.description ? `<p class="line-clamp-2 text-xs text-fg-muted">${escapeHtml(w.description)}</p>` : ''}
        <div class="mt-auto flex items-center justify-between text-[11px] text-fg-dim">
          ${aid
            ? `<span class="flex items-center gap-1.5">${AgentIcon('h-3.5 w-3.5')}<span class="truncate font-mono">${escapeHtml(aid)}</span></span>`
            : '<span></span>'}
          ${canClick ? '<span class="opacity-0 transition group-hover:opacity-100">Abrir →</span>' : ''}
        </div>
      </div>
    </efs-card>
  `;
}

/** @param {EvalRunSummary} run */
function evalBadgeHtml(run) {
  const status = run.status;
  if (status === 'Pending' || status === 'Running') {
    return `<span class="inline-flex items-center rounded-full border border-accent/40 bg-accent-subtle px-2 py-0.5 text-[10px] font-medium text-accent">Avaliação rodando</span>`;
  }
  if (status === 'Failed' || status === 'Cancelled') {
    return `<span class="inline-flex items-center rounded-full border border-danger/40 bg-danger/10 px-2 py-0.5 text-[10px] font-medium text-danger">Avaliação falhou</span>`;
  }
  if (status === 'Completed') {
    const passed = run.casesPassed ?? 0;
    const failed = run.casesFailed ?? 0;
    if (run.casesTotal > 0 && passed + failed === 0) {
      return `<span class="inline-flex items-center rounded-full border border-warning/40 bg-warning/10 px-2 py-0.5 text-[10px] font-medium text-warning">Preset não aplicável</span>`;
    }
    const score = run.avgScore !== null && run.avgScore !== undefined
      ? Math.round(Number(run.avgScore) * 100)
      : null;
    if (score !== null) {
      const isOk = score >= 60;
      const tone = isOk
        ? 'border-success/40 bg-success/10 text-success'
        : 'border-warning/40 bg-warning/10 text-warning';
      const label = isOk ? `Avaliação ✓ ${score}` : `Score baixo ${score}`;
      return `<span class="inline-flex items-center rounded-full border ${tone} px-2 py-0.5 text-[10px] font-medium">${escapeHtml(label)}</span>`;
    }
    return `<span class="inline-flex items-center rounded-full border border-success/40 bg-success/10 px-2 py-0.5 text-[10px] font-medium text-success">Avaliado</span>`;
  }
  return '';
}

// ── Modal "Nova implantação" — picker de agente publicado ──────────────────

let pickerModalEl = /** @type {HTMLElement | null} */ (null);
/** @type {Agent[]} */
let pickerAgents = [];
let pickerLoading = false;
/** @type {string | null} */
let pickerError = null;
let pickerSearch = '';

function openPicker() {
  if (!pickerModalEl) {
    pickerModalEl = document.createElement('efs-modal');
    pickerModalEl.setAttribute('size', 'lg');
    pickerModalEl.setAttribute('title', 'Nova implantação');
    pickerModalEl.setAttribute('description', 'Selecione o agente publicado que você quer implantar.');
    pickerModalEl.addEventListener('close', closePicker);
    document.body.appendChild(pickerModalEl);
  }
  pickerModalEl.setAttribute('open', '');
  loadPickerAgents();
}

function closePicker() {
  pickerModalEl?.removeAttribute('open');
}

async function loadPickerAgents() {
  pickerLoading = true;
  pickerError = null;
  pickerSearch = '';
  renderPicker();
  try {
    const list = await listAgents();
    pickerAgents = list.filter((a) => a.enabled !== false);
  } catch (err) {
    pickerError = friendlyError(err, 'Não foi possível carregar os agentes publicados.');
    pickerAgents = [];
  } finally {
    pickerLoading = false;
    renderPicker();
  }
}

function renderPicker() {
  if (!pickerModalEl) return;

  const deployedAgentIds = new Set(workflows.map(deployedAgentId).filter(Boolean));
  const filtered = filterPickerAgents(pickerAgents, pickerSearch);

  // Body
  let bodyHtml = '';
  if (pickerLoading) {
    bodyHtml = `
      <div class="flex items-center justify-center py-8">
        <efs-spinner class="inline-flex h-6 w-6 text-fg-muted"></efs-spinner>
      </div>`;
  } else if (pickerError) {
    bodyHtml = `<p class="rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">${escapeHtml(pickerError)}</p>`;
  } else if (filtered.length === 0) {
    bodyHtml = '<p class="py-6 text-center text-sm text-fg-muted">Nenhum agente publicado disponível.</p>';
  } else {
    bodyHtml = `
      <ul class="max-h-[400px] space-y-1.5 overflow-y-auto">
        ${filtered.map((a) => {
          const already = deployedAgentIds.has(a.id);
          return `
            <li>
              <button type="button" data-agent-pick="${escapeAttr(a.id)}"
                      class="flex w-full items-center gap-3 rounded-lg border border-border bg-surface px-3 py-2.5 text-left transition hover:border-accent/60 hover:bg-accent-subtle/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40">
                <div class="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
                  ${AgentIcon('h-4 w-4')}
                </div>
                <div class="min-w-0 flex-1">
                  <div class="flex items-center gap-2">
                    <span class="truncate text-sm font-semibold text-fg">${escapeHtml(a.name)}</span>
                    ${already ? '<span class="inline-flex items-center rounded-full border border-success/40 bg-success/10 px-2 py-0.5 text-[10px] font-medium text-success">já implantado</span>' : ''}
                  </div>
                  ${a.description ? `<p class="mt-0.5 line-clamp-1 text-xs text-fg-muted">${escapeHtml(a.description)}</p>` : ''}
                  <p class="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">${escapeHtml(a.id)}</p>
                </div>
              </button>
            </li>
          `;
        }).join('')}
      </ul>
    `;
  }

  pickerModalEl.innerHTML = `
    <div class="space-y-3">
      <div class="relative">
        <span class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">
          ${SearchIcon('h-4 w-4')}
        </span>
        <input id="picker-search" type="text" value="${escapeAttr(pickerSearch)}"
               placeholder="Buscar por nome ou id…"
               class="w-full rounded-lg border border-border bg-surface pl-10 pr-3 py-2 text-sm text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
      </div>
      ${bodyHtml}
    </div>
  `;

  /** @type {HTMLInputElement | null} */
  const pickerInput = pickerModalEl.querySelector('#picker-search');
  pickerInput?.addEventListener('input', () => {
    pickerSearch = pickerInput.value;
    renderPicker();
    // Foco preservado entre re-renders.
    pickerModalEl?.querySelector('#picker-search')?.dispatchEvent(new Event('focus'));
  });

  pickerModalEl.querySelectorAll('[data-agent-pick]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const aid = btn.getAttribute('data-agent-pick');
      if (!aid) return;
      closePicker();
      window.location.assign(`/agentes/${encodeURIComponent(aid)}/implantar`);
    });
  });
}

/**
 * @param {Agent[]} list
 * @param {string} q
 */
function filterPickerAgents(list, q) {
  const trimmed = q.trim().toLowerCase();
  if (!trimmed) return list;
  return list.filter((a) =>
    (a.name ?? '').toLowerCase().includes(trimmed)
    || (a.description ?? '').toLowerCase().includes(trimmed)
    || a.id.toLowerCase().includes(trimmed),
  );
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
