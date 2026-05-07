// @ts-check
/**
 * Página /avaliacoes — sidebar de agentes implantados + histórico de runs
 * + drawer de detalhe de run + drawer de glossário. Substitui
 * mvp/src/routes/Avaliacoes/{index, RunHistory, RunDetailDrawer, GlossaryDrawer}.tsx.
 */

import { listWorkflows, isAgentDeployment, deployedAgentId } from '../lib/workflows.js';
import { listEvalRunsByAgent, listResultsByRun } from '../lib/profile-evaluation.js';
import { isInCurrentProject } from '../lib/project-scope.js';
import { friendlyError } from '../lib/api.js';
import { SearchIcon, SparklesIcon } from '../lib/icons.js';
import { badge, button, errorMessage } from '../lib/ui.js';
import {
  EVALUATOR_CATALOG,
  KIND_DESCRIPTION,
  evaluatorLabel,
  getEvaluatorMeta,
} from '../lib/evaluator-meta.js';

/** @typedef {import('../lib/profile-evaluation.js').EvalRunSummary} EvalRunSummary */
/** @typedef {import('../lib/profile-evaluation.js').EvalResultDetail} EvalResultDetail */

const root = /** @type {HTMLElement} */ (document.getElementById('avaliacoes-page'));

/** @typedef {{ id: string, name: string, workflowId: string, lastRun: EvalRunSummary | null }} AgentEntry */
/** @type {AgentEntry[]} */
let agents = [];
let loading = true;
/** @type {string | null} */
let error = null;
let search = '';

/** @type {string | null} */
let selectedAgentId = new URLSearchParams(window.location.search).get('agent');
/** @type {EvalRunSummary[]} */
let runs = [];
let runsLoading = false;
/** @type {string | null} */
let runsError = null;

/** @type {EvalRunSummary | null} */
let selectedRun = null;
/** @type {EvalResultDetail[]} */
let runResults = [];
let resultsLoading = false;
/** @type {string | null} */
let resultsError = null;
/** @type {'all' | 'passed' | 'failed'} */
let resultFilter = 'all';

let glossaryOpen = false;
/** @type {Set<string>} */
let glossaryHighlight = new Set();
/** @type {'all' | 'Local' | 'Meai'} */
let glossaryFilter = 'all';

renderShell();
loadAgents();

async function loadAgents() {
  loading = true;
  error = null;
  renderBody();
  try {
    const all = await listWorkflows();
    const deployments = all.filter(isAgentDeployment).filter(isInCurrentProject);
    /** @type {AgentEntry[]} */
    const entries = [];
    for (const w of deployments) {
      const aid = deployedAgentId(w);
      if (!aid) continue;
      entries.push({ id: aid, name: w.name, workflowId: w.id, lastRun: null });
    }

    const lastRuns = await Promise.allSettled(
      entries.map((e) => listEvalRunsByAgent(e.id, 1).then((r) => r[0] ?? null)),
    );

    agents = entries.map((e, i) => ({
      ...e,
      lastRun: lastRuns[i].status === 'fulfilled' ? /** @type {any} */ (lastRuns[i]).value : null,
    })).sort((a, b) => {
      const ad = a.lastRun?.createdAt ? new Date(a.lastRun.createdAt).getTime() : 0;
      const bd = b.lastRun?.createdAt ? new Date(b.lastRun.createdAt).getTime() : 0;
      if (bd !== ad) return bd - ad;
      return a.name.localeCompare(b.name);
    });

    if (selectedAgentId && agents.some((a) => a.id === selectedAgentId)) {
      loadRunsFor(selectedAgentId);
    }
  } catch (err) {
    error = friendlyError(err, 'Não foi possível carregar as implantações.');
  } finally {
    loading = false;
    renderBody();
  }
}

/** @param {string} agentId */
async function loadRunsFor(agentId) {
  runsLoading = true;
  runsError = null;
  renderBody();
  try {
    const list = await listEvalRunsByAgent(agentId, 50);
    runs = [...list].sort((a, b) =>
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
    );
  } catch (err) {
    runsError = friendlyError(err, 'Não foi possível carregar o histórico de avaliações.');
    runs = [];
  } finally {
    runsLoading = false;
    renderBody();
  }
}

function renderShell() {
  root.innerHTML = `
    <div class="mb-6 flex items-end justify-between gap-4">
      <div>
        <h1 class="text-[28px] font-semibold tracking-tight text-fg">Avaliações</h1>
        <p class="mt-2 text-sm text-fg-muted">
          Histórico de avaliações automáticas por agente. Cada implantação dispara uma run no preset escolhido.
        </p>
      </div>
      ${button({
        label: 'O que cada métrica significa',
        variant: 'secondary',
        size: 'sm',
        leftIcon: SparklesIcon('h-4 w-4'),
        attrs: { 'data-open-glossary': true },
      })}
    </div>
    <div id="avaliacoes-body"></div>
    <div id="drawers-host"></div>
  `;
  root.querySelector('[data-open-glossary]')?.addEventListener('click', () => openGlossary([]));
}

function renderBody() {
  const body = /** @type {HTMLElement} */ (root.querySelector('#avaliacoes-body'));
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
    body.innerHTML = errorMessage(error);
    return;
  }

  if (agents.length === 0) {
    body.innerHTML = `
      <efs-card>
        <div class="text-center">
          <h3 class="text-sm font-semibold text-fg">Nenhum agente implantado</h3>
          <p class="mt-1 text-xs text-fg-muted">Implante um agente em <strong>Implantações</strong> pra que as avaliações automáticas comecem a rodar.</p>
        </div>
      </efs-card>
    `;
    return;
  }

  const selected = agents.find((a) => a.id === selectedAgentId) ?? null;

  body.innerHTML = `
    <div class="grid grid-cols-1 gap-5 lg:grid-cols-[280px_1fr]">
      <aside class="space-y-3">
        <div class="relative">
          <span class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">${SearchIcon('h-4 w-4')}</span>
          <input id="search-input" type="text" placeholder="Buscar agente…" value="${escapeAttr(search)}"
                 class="w-full rounded-lg border border-border bg-surface pl-10 pr-3 py-2 text-sm text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
        </div>
        <nav class="space-y-1.5" id="agents-nav"></nav>
      </aside>
      <main class="space-y-3" id="runs-main">
        ${selected ? selectedAgentSummary(selected) : ''}
        <div id="runs-list"></div>
      </main>
    </div>
  `;

  /** @type {HTMLInputElement | null} */
  const searchInput = body.querySelector('#search-input');
  searchInput?.addEventListener('input', () => {
    search = searchInput.value;
    renderAgentsNav();
  });

  renderAgentsNav();
  renderRunsList();
}

function renderAgentsNav() {
  const nav = /** @type {HTMLElement} */ (root.querySelector('#agents-nav'));
  if (!nav) return;

  const trimmed = search.trim().toLowerCase();
  const filtered = trimmed
    ? agents.filter((a) => a.name.toLowerCase().includes(trimmed) || a.id.toLowerCase().includes(trimmed))
    : agents;

  if (filtered.length === 0) {
    nav.innerHTML = '<p class="px-2 py-3 text-xs text-fg-muted">Nada bate com a busca.</p>';
    return;
  }

  nav.innerHTML = filtered.map((a) => agentRowHtml(a, a.id === selectedAgentId)).join('');
  nav.querySelectorAll('[data-agent-row]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const id = btn.getAttribute('data-agent-row');
      if (!id) return;
      handleSelectAgent(id);
    });
  });
}

/** @param {AgentEntry} a @param {boolean} active */
function agentRowHtml(a, active) {
  const score = a.lastRun?.avgScore !== null && a.lastRun?.avgScore !== undefined
    ? Math.round(Number(a.lastRun.avgScore) * 100)
    : null;
  const status = a.lastRun?.status;
  const stateClasses = active
    ? 'border-accent bg-accent-subtle text-fg'
    : 'border-border bg-surface text-fg hover:bg-surface-hover';

  let scoreBadge = '';
  if (score !== null) {
    const tone = score >= 80 ? 'success' : score >= 60 ? 'neutral' : 'warning';
    scoreBadge = badge(String(score), { tone, extraClasses: 'shrink-0' });
  }
  const dateLine = a.lastRun
    ? (status === 'Running' || status === 'Pending'
        ? 'Avaliação em andamento'
        : `Última: ${escapeHtml(relativeDate(a.lastRun.createdAt))}`)
    : 'Sem avaliações ainda';

  return `
    <button data-agent-row="${escapeAttr(a.id)}"
            class="w-full rounded-lg border px-3 py-2.5 text-left transition ${stateClasses}">
      <div class="flex items-start justify-between gap-2">
        <div class="min-w-0 flex-1">
          <p class="truncate text-sm font-semibold">${escapeHtml(a.name)}</p>
          <p class="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">${escapeHtml(a.id)}</p>
        </div>
        ${scoreBadge}
      </div>
      <p class="mt-1.5 text-[10px] text-fg-dim">${dateLine}</p>
    </button>
  `;
}

/** @param {AgentEntry} selected */
function selectedAgentSummary(selected) {
  const score = selected.lastRun?.avgScore !== null && selected.lastRun?.avgScore !== undefined
    ? Math.round(Number(selected.lastRun.avgScore) * 100)
    : null;
  return `
    <efs-card>
      <div class="flex items-baseline justify-between gap-3">
        <div class="min-w-0">
          <h2 class="truncate text-base font-semibold text-fg">${escapeHtml(selected.name)}</h2>
          <p class="font-mono text-[10px] uppercase tracking-wider text-fg-dim">${escapeHtml(selected.id)}</p>
        </div>
        ${score !== null ? `
          <div class="text-right">
            <div class="text-2xl font-bold text-fg">${score}</div>
            <div class="text-[10px] text-fg-dim">último score /100</div>
          </div>` : ''}
      </div>
    </efs-card>
  `;
}

function renderRunsList() {
  const list = /** @type {HTMLElement} */ (root.querySelector('#runs-list'));
  if (!list) return;

  if (!selectedAgentId) {
    list.innerHTML = `
      <efs-card>
        <p class="text-center text-sm text-fg-muted">Selecione um agente à esquerda para ver o histórico de avaliações.</p>
      </efs-card>
    `;
    return;
  }

  if (runsLoading) {
    list.innerHTML = `
      <efs-card>
        <div class="flex items-center justify-center py-12">
          <efs-spinner class="inline-flex h-5 w-5 text-fg-muted"></efs-spinner>
        </div>
      </efs-card>
    `;
    return;
  }

  if (runsError) {
    list.innerHTML = errorMessage(runsError);
    return;
  }

  const selected = agents.find((a) => a.id === selectedAgentId);

  if (runs.length === 0) {
    list.innerHTML = `
      <efs-card>
        <div class="text-center">
          <h3 class="text-sm font-semibold text-fg">Sem avaliações ainda</h3>
          <p class="mt-1 text-xs text-fg-muted">${escapeHtml(selected?.name ?? 'Este agente')} ainda não foi avaliado. Ao implantar uma versão, o auto-deploy dispara a primeira avaliação automaticamente.</p>
        </div>
      </efs-card>
    `;
    return;
  }

  list.innerHTML = `<div class="space-y-2">${runs.map(runRowHtml).join('')}</div>`;

  list.querySelectorAll('[data-run-id]').forEach((row) => {
    const id = row.getAttribute('data-run-id');
    if (!id) return;
    /** @param {Event | KeyboardEvent} e */
    const open = (e) => {
      if ('key' in e && e.key !== 'Enter' && e.key !== ' ') return;
      e.preventDefault();
      const run = runs.find((r) => r.runId === id);
      if (run) openRunDetail(run);
    };
    row.addEventListener('click', open);
    row.addEventListener('keydown', open);
  });
}

/** @param {EvalRunSummary} run */
function runRowHtml(run) {
  const preset = run.triggerContext?.preset ?? 'manual';
  const score = run.avgScore !== null && run.avgScore !== undefined
    ? Math.round(Number(run.avgScore) * 100)
    : null;
  const failed = run.casesFailed ?? 0;
  const passed = run.casesPassed ?? 0;
  const total = run.casesTotal;
  const allSkipped = run.status === 'Completed' && total > 0 && passed + failed === 0;

  const statusBadge = statusBadgeFor(run.status);
  const dot = dotColor(run);

  const rightSide = allSkipped
    ? badge('tente Média', { tone: 'warning' })
    : score !== null
      ? `<div class="text-right">
           <div class="text-2xl font-bold ${score >= 80 ? 'text-success' : score >= 60 ? 'text-fg' : 'text-warning'}">${score}</div>
           <div class="text-[10px] text-fg-dim">/100</div>
         </div>`
      : badge('sem score', { tone: 'neutral' });

  const meta = allSkipped
    ? '<span>Todos os evaluators pularam — agente não casa com este preset.</span>'
    : `${total} cases · ${passed} OK · ${failed} falhas${run.lastError ? ` · <span class="text-danger">${escapeHtml(truncate(run.lastError, 80))}</span>` : ''}`;

  return `
    <efs-card interactive>
      <div data-run-id="${escapeAttr(run.runId)}" role="button" tabindex="0" class="cursor-pointer">
        <div class="flex flex-wrap items-center gap-3">
          <span class="h-2 w-2 shrink-0 rounded-full ${dot}"></span>
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <span class="text-sm font-semibold text-fg">${escapeHtml(formatDateTime(run.createdAt))}</span>
              ${badge(`preset ${preset}`, { tone: 'accent' })}
              ${statusBadge}
              ${allSkipped ? badge('preset não aplicável', { tone: 'warning' }) : ''}
            </div>
            <p class="mt-1 text-[11px] text-fg-muted">${meta}</p>
          </div>
          ${rightSide}
        </div>
      </div>
    </efs-card>
  `;
}

/** @param {string} status */
function statusBadgeFor(status) {
  if (status === 'Completed') return badge('Concluído', { tone: 'success' });
  if (status === 'Running')   return badge('Rodando', { tone: 'accent' });
  if (status === 'Pending')   return badge('Aguardando', { tone: 'neutral' });
  if (status === 'Failed')    return badge('Falhou', { tone: 'danger' });
  if (status === 'Cancelled') return badge('Cancelado', { tone: 'neutral' });
  return badge(status, { tone: 'neutral' });
}

/** @param {EvalRunSummary} run */
function dotColor(run) {
  if (run.status === 'Failed' || run.status === 'Cancelled') return 'bg-danger';
  if (run.status === 'Running' || run.status === 'Pending') return 'bg-accent';
  if (run.avgScore !== null && run.avgScore !== undefined) {
    const s = Number(run.avgScore) * 100;
    if (s >= 80) return 'bg-success';
    if (s >= 60) return 'bg-fg-muted';
    return 'bg-warning';
  }
  return 'bg-fg-dim';
}

/** @param {string} agentId */
function handleSelectAgent(agentId) {
  selectedAgentId = agentId;
  const url = new URL(window.location.href);
  url.searchParams.set('agent', agentId);
  window.history.replaceState({}, '', url.toString());
  selectedRun = null;
  loadRunsFor(agentId);
}

// ── RunDetail Drawer ──────────────────────────────────────────────────────

/** @type {HTMLElement | null} */
let runDrawerEl = null;

/** @param {EvalRunSummary} run */
async function openRunDetail(run) {
  selectedRun = run;
  runResults = [];
  resultsLoading = true;
  resultsError = null;
  resultFilter = 'all';

  if (!runDrawerEl) {
    runDrawerEl = document.createElement('efs-drawer');
    runDrawerEl.setAttribute('width', '640px');
    runDrawerEl.setAttribute('z-index', '40');
    runDrawerEl.addEventListener('close', closeRunDetail);
    document.body.appendChild(runDrawerEl);
  }

  runDrawerEl.setAttribute('eyebrow', 'Avaliação');
  runDrawerEl.setAttribute('title', formatRunHeader(run));
  runDrawerEl.setAttribute('open', '');
  renderRunDrawer();

  try {
    runResults = await listResultsByRun(run.runId);
  } catch (err) {
    resultsError = friendlyError(err, 'Não foi possível carregar os resultados.');
  } finally {
    resultsLoading = false;
    renderRunDrawer();
  }
}

function closeRunDetail() {
  runDrawerEl?.removeAttribute('open');
  selectedRun = null;
}

function renderRunDrawer() {
  if (!runDrawerEl || !selectedRun) return;
  const run = selectedRun;
  const preset = run.triggerContext?.preset ?? 'manual';
  const score = run.avgScore !== null && run.avgScore !== undefined
    ? Math.round(Number(run.avgScore) * 100)
    : null;

  const evaluatorNames = Array.from(new Set(runResults.map((r) => r.evaluatorName)));
  const passedCount = runResults.filter((r) => r.passed).length;
  const failedCount = runResults.filter((r) => !r.passed).length;

  const filtered = resultFilter === 'all'
    ? runResults
    : resultFilter === 'passed'
      ? runResults.filter((r) => r.passed)
      : runResults.filter((r) => !r.passed);

  /** @type {Map<string, EvalResultDetail[]>} */
  const byCase = new Map();
  for (const r of filtered) {
    const list = byCase.get(r.caseId) ?? [];
    list.push(r);
    byCase.set(r.caseId, list);
  }

  const filterButtons = [
    ['all', `Todas (${runResults.length})`],
    ['failed', `Falhas (${failedCount})`],
    ['passed', `OK (${passedCount})`],
  ].map(([k, label]) => {
    const active = resultFilter === k;
    const stateClasses = active
      ? 'border-accent bg-accent-subtle text-accent'
      : 'border-border bg-surface text-fg-muted hover:bg-surface-hover hover:text-fg';
    return `<button data-filter="${k}" class="rounded-full border px-2.5 py-1 text-[11px] font-medium transition ${stateClasses}">${escapeHtml(label)}</button>`;
  }).join('');

  let resultsHtml = '';
  if (resultsLoading) {
    resultsHtml = '<div class="flex items-center justify-center py-10"><efs-spinner class="inline-flex h-5 w-5 text-fg-muted"></efs-spinner></div>';
  } else if (resultsError) {
    resultsHtml = `<div class="p-4">${errorMessage(resultsError)}</div>`;
  } else if (byCase.size === 0) {
    resultsHtml = '<div class="px-4 py-10 text-center text-sm text-fg-muted">Sem avaliações para o filtro escolhido.</div>';
  } else {
    resultsHtml = Array.from(byCase.entries()).map(([caseId, items]) => caseCardHtml(caseId, items)).join('');
  }

  // Conteúdo do drawer (reset)
  runDrawerEl.innerHTML = `
    <div data-efs-header-actions>
      ${button({
        label: 'Glossário',
        variant: 'ghost',
        size: 'sm',
        leftIcon: SparklesIcon('h-3.5 w-3.5'),
        attrs: { 'data-open-glossary': true },
      })}
    </div>
    <div class="px-4 py-3 border-b border-border">
      <div class="flex flex-wrap items-center gap-2">
        ${badge(`preset ${preset}`, { tone: 'accent' })}
        <span class="text-[11px] text-fg-dim">${run.casesTotal} cases · ${runResults.length} avaliações</span>
        ${score !== null ? badge(`Score ${score}/100`, { tone: score >= 60 ? 'success' : 'warning' }) : ''}
      </div>
    </div>
    <div class="flex shrink-0 flex-wrap items-center justify-between gap-2 border-b border-border px-4 py-2.5">
      <div class="flex flex-wrap gap-1.5">${filterButtons}</div>
    </div>
    <div>${resultsHtml}</div>
  `;

  // Wire
  runDrawerEl.querySelector('[data-open-glossary]')?.addEventListener('click', () => openGlossary(evaluatorNames));
  runDrawerEl.querySelectorAll('[data-filter]').forEach((btn) => {
    btn.addEventListener('click', () => {
      resultFilter = /** @type {any} */ (btn.getAttribute('data-filter'));
      renderRunDrawer();
    });
  });
  runDrawerEl.querySelectorAll('[data-toggle-case]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const target = btn.nextElementSibling;
      if (target instanceof HTMLElement) {
        target.classList.toggle('hidden');
        const arrow = btn.querySelector('[data-arrow]');
        if (arrow) arrow.classList.toggle('rotate-90');
      }
    });
  });
}

/** @param {string} caseId @param {EvalResultDetail[]} items */
function caseCardHtml(caseId, items) {
  const passedCount = items.filter((r) => r.passed).length;
  const totalCount = items.length;
  const allPassed = passedCount === totalCount;
  const noPassed = passedCount === 0;
  const dot = allPassed ? 'bg-success' : noPassed ? 'bg-danger' : 'bg-warning';

  return `
    <article class="border-b border-border">
      <button data-toggle-case class="flex w-full items-center gap-3 px-4 py-3 text-left transition hover:bg-surface-hover">
        <span class="h-2 w-2 shrink-0 rounded-full ${dot}"></span>
        <div class="min-w-0 flex-1">
          <p class="font-mono text-[10px] uppercase tracking-wider text-fg-dim">case ${escapeHtml(caseId.slice(0, 8))}</p>
          <p class="mt-0.5 truncate text-sm text-fg">${passedCount}/${totalCount} avaliações OK</p>
        </div>
        <span data-arrow class="text-fg-dim transition-transform">›</span>
      </button>
      <div class="hidden space-y-2 px-4 pb-3">
        ${items.map(resultRowHtml).join('')}
      </div>
    </article>
  `;
}

/** @param {EvalResultDetail} r */
function resultRowHtml(r) {
  const meta = getEvaluatorMeta(r.evaluatorName);
  const score = r.score !== null ? Math.round(Number(r.score) * 100) : null;
  const stateClasses = r.passed ? 'border-success/30 bg-success/5' : 'border-danger/30 bg-danger/5';

  return `
    <div class="rounded-lg border bg-surface p-3 ${stateClasses}">
      <div class="flex flex-wrap items-center gap-2">
        ${badge(meta?.kind ?? '?', { tone: meta?.kind === 'Meai' ? 'accent' : 'neutral' })}
        <span class="text-sm font-semibold text-fg">${escapeHtml(evaluatorLabel(r.evaluatorName))}</span>
        ${score !== null ? badge(`${score}/100`, { tone: r.passed ? 'success' : 'warning' }) : ''}
        <span class="text-[10px] text-fg-dim">${r.passed ? 'OK' : 'falhou'}</span>
        ${r.repetitionIndex > 0 ? `<span class="text-[10px] text-fg-dim">rep #${r.repetitionIndex}</span>` : ''}
      </div>
      ${r.reason ? `<p class="mt-1.5 text-xs leading-relaxed text-fg-muted">${escapeHtml(r.reason)}</p>` : ''}
      ${r.judgeModel ? `<p class="mt-1 font-mono text-[10px] text-fg-dim">judge: ${escapeHtml(r.judgeModel)}</p>` : ''}
    </div>
  `;
}

// ── Glossary Drawer ───────────────────────────────────────────────────────

/** @type {HTMLElement | null} */
let glossaryDrawerEl = null;

/** @param {string[]} highlight */
function openGlossary(highlight) {
  glossaryHighlight = new Set(highlight);
  glossaryFilter = 'all';
  glossaryOpen = true;

  if (!glossaryDrawerEl) {
    glossaryDrawerEl = document.createElement('efs-drawer');
    glossaryDrawerEl.setAttribute('width', '480px');
    glossaryDrawerEl.setAttribute('z-index', '50');
    glossaryDrawerEl.setAttribute('eyebrow', 'Glossário');
    glossaryDrawerEl.setAttribute('title', 'O que cada métrica avalia');
    glossaryDrawerEl.setAttribute('description', 'Mostramos abaixo todos os evaluators que o auto-deploy usa nos presets Básica/Média/Avançada.');
    glossaryDrawerEl.addEventListener('close', () => {
      glossaryOpen = false;
      glossaryDrawerEl?.removeAttribute('open');
    });
    document.body.appendChild(glossaryDrawerEl);
  }

  glossaryDrawerEl.setAttribute('open', '');
  renderGlossary();
}

function renderGlossary() {
  if (!glossaryDrawerEl || !glossaryOpen) return;

  const filterTabs = [
    ['all', 'Todos', EVALUATOR_CATALOG.length],
    ['Local', 'Local (heurísticas)', EVALUATOR_CATALOG.filter((e) => e.kind === 'Local').length],
    ['Meai', 'MEAI (LLM-as-judge)', EVALUATOR_CATALOG.filter((e) => e.kind === 'Meai').length],
  ].map(([k, label, count]) => {
    const active = glossaryFilter === k;
    const stateClasses = active
      ? 'border-accent bg-accent-subtle text-accent'
      : 'border-border bg-surface text-fg-muted hover:bg-surface-hover hover:text-fg';
    return `<button data-glossary-filter="${k}" class="rounded-full border px-2.5 py-1 text-[11px] font-medium transition ${stateClasses}">${escapeHtml(/** @type {string} */ (label))} <span class="text-fg-dim">${count}</span></button>`;
  }).join('');

  /** @type {Array<'Local' | 'Meai'>} */
  const kinds = ['Local', 'Meai'];
  const sectionsHtml = kinds
    .filter((k) => glossaryFilter === 'all' || glossaryFilter === k)
    .map((kind) => {
      const items = EVALUATOR_CATALOG.filter((e) => e.kind === kind);
      if (items.length === 0) return '';
      return `
        <section class="border-b border-border last:border-b-0">
          <header class="bg-bg-soft px-4 py-2.5">
            <div class="flex items-center gap-2">
              ${badge(kind, { tone: kind === 'Meai' ? 'accent' : 'neutral' })}
              <span class="text-xs font-semibold text-fg">${kind === 'Local' ? 'Heurísticas' : 'LLM-as-judge'}</span>
            </div>
            <p class="mt-1 text-[11px] leading-relaxed text-fg-muted">${escapeHtml(KIND_DESCRIPTION[kind])}</p>
          </header>
          <ul class="divide-y divide-border">
            ${items.map((e) => {
              const isHighlighted = glossaryHighlight.has(e.name);
              return `
                <li class="space-y-1.5 px-4 py-3 ${isHighlighted ? 'bg-accent-subtle/30' : ''}">
                  <div class="flex flex-wrap items-center gap-1.5">
                    <span class="text-sm font-semibold text-fg">${escapeHtml(e.label)}</span>
                    <span class="font-mono text-[10px] text-fg-dim">${escapeHtml(e.name)}</span>
                    ${isHighlighted ? badge('usado nesta run', { tone: 'accent' }) : ''}
                  </div>
                  <p class="text-xs leading-relaxed text-fg-muted">${escapeHtml(e.fullDescription)}</p>
                  <p class="text-[11px] text-fg-dim">
                    <span class="font-semibold text-fg-muted">Score:</span> ${escapeHtml(e.scoreMeaning)}
                  </p>
                </li>
              `;
            }).join('')}
          </ul>
        </section>
      `;
    }).join('');

  glossaryDrawerEl.innerHTML = `
    <div class="flex shrink-0 flex-wrap gap-1.5 border-b border-border px-4 py-2.5">${filterTabs}</div>
    <div>${sectionsHtml}</div>
  `;

  glossaryDrawerEl.querySelectorAll('[data-glossary-filter]').forEach((btn) => {
    btn.addEventListener('click', () => {
      glossaryFilter = /** @type {any} */ (btn.getAttribute('data-glossary-filter'));
      renderGlossary();
    });
  });
}

// ── Helpers ───────────────────────────────────────────────────────────────

/** @param {EvalRunSummary} run */
function formatRunHeader(run) {
  if (!run.startedAt) return `Run · ${run.runId.slice(0, 8)}`;
  return new Date(run.startedAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

/** @param {string} iso */
function formatDateTime(iso) {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

/** @param {string} iso */
function relativeDate(iso) {
  const diff = Date.now() - new Date(iso).getTime();
  const min = Math.floor(diff / 60_000);
  if (min < 1) return 'agora';
  if (min < 60) return `${min}min atrás`;
  const h = Math.floor(min / 60);
  if (h < 24) return `${h}h atrás`;
  const d = Math.floor(h / 24);
  if (d < 7) return `${d}d atrás`;
  return new Date(iso).toLocaleDateString('pt-BR', { dateStyle: 'short' });
}

/** @param {string} s @param {number} n */
function truncate(s, n) {
  return s.length > n ? `${s.slice(0, n)}…` : s;
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
