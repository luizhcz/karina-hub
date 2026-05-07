// @ts-check
/**
 * Página /agentes/:id/versoes — lista de versões publicadas do agente +
 * comparação side-by-side de até 2 versões. Substitui
 * mvp/src/routes/AgentVersions.tsx.
 *
 * URL preservada via nginx rewrite — id vem de window.location.pathname.
 */

import { getAgent } from '../lib/agents.js';
import { listAgentVersions } from '../lib/agent-versions.js';
import { friendlyError } from '../lib/api.js';
import { ArrowLeftIcon } from '../lib/icons.js';
import { badge, button, errorMessage } from '../lib/ui.js';

/** @typedef {import('../lib/agents.js').Agent} Agent */
/** @typedef {import('../lib/agent-versions.js').AgentVersion} AgentVersion */

const MAX_SELECTED = 2;
const root = /** @type {HTMLElement} */ (document.getElementById('versoes-page'));

const agentId = window.location.pathname.split('/')[2] ?? '';

/** @type {Agent | null} */
let agent = null;
/** @type {AgentVersion[]} */
let versions = [];
let loading = true;
/** @type {string | null} */
let error = null;
/** @type {string[]} */
let selected = [];

if (!agentId) {
  showError('ID do agente ausente na URL.');
} else {
  loadAll();
}

async function loadAll() {
  loading = true;
  error = null;
  renderShell();
  try {
    const [a, vs] = await Promise.all([getAgent(agentId), listAgentVersions(agentId)]);
    agent = a;
    versions = vs;
    if (vs.length >= 2) selected = [vs[0].agentVersionId, vs[1].agentVersionId];
    else if (vs.length === 1) selected = [vs[0].agentVersionId];
  } catch (err) {
    error = friendlyError(err, 'Não foi possível carregar as versões deste agente.');
  } finally {
    loading = false;
    renderShell();
  }
}

/** @param {string} message */
function showError(message) {
  root.innerHTML = errorMessage(message);
}

function renderShell() {
  if (loading) {
    root.innerHTML = `
      <efs-card>
        <div class="flex items-center justify-center py-12">
          <efs-spinner class="inline-flex h-6 w-6 text-fg-muted"></efs-spinner>
        </div>
      </efs-card>
    `;
    return;
  }

  if (error) {
    root.innerHTML = errorMessage(error);
    return;
  }

  const titleSuffix = agent?.name ?? agentId;

  root.innerHTML = `
    <div class="flex items-center gap-3">
      ${button({
        label: 'Voltar',
        variant: 'ghost',
        size: 'sm',
        leftIcon: ArrowLeftIcon('h-4 w-4'),
        attrs: { 'data-back': true },
      })}
      <div class="min-w-0 flex-1">
        <h1 class="truncate text-2xl font-semibold tracking-tight text-fg">Versões — ${escapeHtml(titleSuffix)}</h1>
        <p class="mt-1 text-xs text-fg-muted">Selecione até duas versões para comparar campo a campo.</p>
      </div>
    </div>

    <div id="versions-list"></div>
    <div id="compare-panel"></div>
  `;

  root.querySelector('[data-back]')?.addEventListener('click', () => {
    window.location.assign('/agentes?tab=published');
  });

  renderVersionsList();
  renderComparePanel();
}

function renderVersionsList() {
  const list = /** @type {HTMLElement} */ (root.querySelector('#versions-list'));
  if (!list) return;

  if (versions.length === 0) {
    list.innerHTML = `
      <efs-card>
        <p class="text-center text-sm text-fg-muted">
          Nenhuma versão registrada ainda. Toda aprovação de rascunho gera uma nova versão.
        </p>
      </efs-card>
    `;
    return;
  }

  list.innerHTML = `
    <div class="space-y-2">
      ${versions.map(versionRowHtml).join('')}
    </div>
  `;

  list.querySelectorAll('[data-version-id]').forEach((row) => {
    const id = row.getAttribute('data-version-id');
    if (!id) return;
    row.addEventListener('click', () => toggleSelect(id));
  });
}

/** @param {AgentVersion} v */
function versionRowHtml(v) {
  const isSelected = selected.includes(v.agentVersionId);
  const order = selected.indexOf(v.agentVersionId);
  const stateClasses = isSelected ? 'border-accent ring-2 ring-accent/30' : 'border-border hover:border-accent/50';
  const avatarClasses = isSelected ? 'bg-accent text-accent-contrast' : 'bg-bg-soft text-fg-muted';
  const avatarLabel = isSelected ? (order === 0 ? 'A' : 'B') : `r${v.revision}`;
  const breakingBadge = v.breakingChange
    ? badge('Breaking', { tone: 'warning' })
    : badge('Patch', { tone: 'success' });

  return `
    <button type="button" data-version-id="${escapeAttr(v.agentVersionId)}"
            class="group flex w-full items-center gap-4 rounded-lg border bg-surface px-4 py-3 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40 ${stateClasses}">
      <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${avatarClasses}">${avatarLabel}</div>
      <div class="min-w-0 flex-1">
        <div class="flex flex-wrap items-center gap-2">
          <span class="font-mono text-sm font-semibold text-fg">Revisão ${v.revision}</span>
          ${breakingBadge}
          <span class="font-mono text-[10px] uppercase tracking-wider text-fg-dim">${escapeHtml(v.contentHash.slice(0, 12))}</span>
        </div>
        <p class="mt-0.5 text-xs text-fg-muted">
          por <span class="font-mono">${escapeHtml(v.createdBy ?? 'system')}</span> · ${escapeHtml(formatAbsolute(v.createdAt))}
        </p>
        ${v.changeReason ? `<p class="mt-1 line-clamp-2 text-xs text-fg">${escapeHtml(v.changeReason)}</p>` : ''}
      </div>
    </button>
  `;
}

/** @param {string} versionId */
function toggleSelect(versionId) {
  if (selected.includes(versionId)) {
    selected = selected.filter((v) => v !== versionId);
  } else if (selected.length >= MAX_SELECTED) {
    selected = [selected[1], versionId];
  } else {
    selected = [...selected, versionId];
  }
  renderVersionsList();
  renderComparePanel();
}

function renderComparePanel() {
  const panel = /** @type {HTMLElement} */ (root.querySelector('#compare-panel'));
  if (!panel) return;

  if (selected.length !== 2) {
    panel.innerHTML = '';
    return;
  }
  const a = versions.find((v) => v.agentVersionId === selected[0]);
  const b = versions.find((v) => v.agentVersionId === selected[1]);
  if (!a || !b) {
    panel.innerHTML = '';
    return;
  }

  const left = a.revision < b.revision ? a : b;
  const right = a.revision < b.revision ? b : a;
  const diffs = buildDiff(left, right);
  const same = diffs.length === 0;

  panel.innerHTML = `
    <efs-card>
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Comparando r${left.revision} → r${right.revision}</h3>
          <p class="mt-1 text-xs text-fg-muted">${same ? 'Snapshots idênticos no nível de campo.' : `${diffs.length} ${diffs.length === 1 ? 'campo' : 'campos'} com diferença.`}</p>
        </div>
        ${same
          ? '<p class="text-sm text-fg-muted">As duas revisões têm o mesmo conteúdo lógico (mesmo ContentHash do ponto de vista dos campos versionados).</p>'
          : `<div class="space-y-4">${diffs.map(diffFieldHtml).join('')}</div>`
        }
      </div>
    </efs-card>
  `;
}

/**
 * @typedef {{ label: string, left: string, right: string, multiline?: boolean }} DiffEntry
 *
 * @param {AgentVersion} a
 * @param {AgentVersion} b
 * @returns {DiffEntry[]}
 */
function buildDiff(a, b) {
  /** @type {DiffEntry[]} */
  const out = [];
  push(out, 'Prompt (instructions)', a.promptContent ?? '', b.promptContent ?? '', true);
  push(out, 'Description', a.description ?? '', b.description ?? '', true);
  push(out, 'Model · deployment', a.model?.deploymentName ?? '', b.model?.deploymentName ?? '');
  push(out, 'Model · temperature', stringifyNum(a.model?.temperature), stringifyNum(b.model?.temperature));
  push(out, 'Model · maxTokens', stringifyNum(a.model?.maxTokens), stringifyNum(b.model?.maxTokens));
  push(out, 'Provider · type', a.provider?.type ?? '', b.provider?.type ?? '');
  push(out, 'Provider · clientType', a.provider?.clientType ?? '', b.provider?.clientType ?? '');
  push(out, 'Provider · endpoint', a.provider?.endpoint ?? '', b.provider?.endpoint ?? '');
  push(out, 'Tools', toolsLabel(a.tools), toolsLabel(b.tools), true);
  push(out, 'Output schema', a.outputSchema?.schemaJson ?? a.outputSchema?.schemaName ?? '',
                              b.outputSchema?.schemaJson ?? b.outputSchema?.schemaName ?? '', true);
  push(out, 'Breaking change', String(a.breakingChange), String(b.breakingChange));
  push(out, 'Change reason', a.changeReason ?? '', b.changeReason ?? '', true);
  return out;
}

/**
 * @param {DiffEntry[]} out
 * @param {string} label
 * @param {string} left
 * @param {string} right
 * @param {boolean} [multiline]
 */
function push(out, label, left, right, multiline = false) {
  if (left === right) return;
  out.push({ label, left, right, multiline });
}

/** @param {DiffEntry} d */
function diffFieldHtml(d) {
  return `
    <div class="rounded-lg border border-border">
      <div class="border-b border-border bg-bg-soft px-3 py-1.5 text-[11px] font-semibold uppercase tracking-wider text-fg-muted">${escapeHtml(d.label)}</div>
      <div class="grid grid-cols-1 gap-px bg-border md:grid-cols-2">
        ${diffSideHtml(d.left, 'left', d.multiline)}
        ${diffSideHtml(d.right, 'right', d.multiline)}
      </div>
    </div>
  `;
}

/**
 * @param {string} value
 * @param {'left' | 'right'} tone
 * @param {boolean} [multiline]
 */
function diffSideHtml(value, tone, multiline) {
  const empty = value.length === 0;
  const bg = tone === 'left' ? 'bg-danger/5' : 'bg-success/5';
  if (empty) {
    return `<div class="px-3 py-2 text-xs ${bg}"><span class="italic text-fg-dim">∅</span></div>`;
  }
  if (multiline) {
    return `<div class="px-3 py-2 text-xs ${bg}"><pre class="whitespace-pre-wrap break-words font-mono text-[11px] leading-relaxed text-fg">${escapeHtml(value)}</pre></div>`;
  }
  return `<div class="px-3 py-2 text-xs ${bg}"><span class="font-mono text-fg">${escapeHtml(value)}</span></div>`;
}

/** @param {number | null | undefined} n */
function stringifyNum(n) {
  if (n === null || n === undefined) return '';
  return String(n);
}

/** @param {AgentVersion['tools']} tools */
function toolsLabel(tools) {
  if (!tools || tools.length === 0) return '';
  return tools.map((t) => `${t.type}${t.name ? `:${t.name}` : ''}`).join('\n');
}

/** @param {string} iso */
function formatAbsolute(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
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
