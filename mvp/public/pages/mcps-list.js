// @ts-check
/**
 * Página /mcps — lista MCP servers do projeto. Substitui
 * mvp/src/routes/McpServersList.tsx.
 */

import { listMcpServers } from '../lib/mcp-servers.js';
import { friendlyError } from '../lib/api.js';
import { PlusIcon, SearchIcon, ServerIcon } from '../lib/icons.js';

/** @typedef {import('../lib/mcp-servers.js').McpServer} McpServer */

const root = /** @type {HTMLElement} */ (document.getElementById('mcps-page'));

/** @type {McpServer[]} */
let items = [];
let loading = true;
/** @type {string | null} */
let error = null;
let search = '';

renderShell();
loadItems();

async function loadItems() {
  loading = true;
  renderBody();
  try {
    items = await listMcpServers();
    error = null;
  } catch (err) {
    error = friendlyError(err, 'Não foi possível carregar os MCPs.');
    items = [];
  } finally {
    loading = false;
    renderBody();
  }
}

function renderShell() {
  root.innerHTML = `
    <div class="mb-8 flex items-end justify-between">
      <div>
        <h1 class="text-[28px] font-semibold tracking-tight text-fg">MCPs</h1>
        <p class="mt-2 text-sm text-fg-muted">
          Servidores Model Context Protocol que seus agentes podem consumir. O agente referencia
          pelo Id e o runtime resolve label, URL e tools permitidas em cada execução.
        </p>
      </div>
      <a href="/mcps/novo"
         class="inline-flex items-center gap-2 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-contrast hover:bg-accent-soft">
        ${PlusIcon('h-4 w-4')}
        <span>Novo MCP</span>
      </a>
    </div>

    <div class="mb-6 max-w-md">
      <div class="relative">
        <span class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">
          ${SearchIcon('h-4 w-4')}
        </span>
        <input id="search-input" type="text"
               placeholder="Buscar por nome, label ou URL…"
               class="w-full rounded-lg border border-border bg-surface pl-10 pr-3 py-2 text-sm text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
      </div>
    </div>

    <div id="mcps-body"></div>
  `;

  /** @type {HTMLInputElement | null} */
  const searchInput = root.querySelector('#search-input');
  searchInput?.addEventListener('input', () => {
    search = searchInput.value;
    renderBody();
  });
}

function renderBody() {
  const body = /** @type {HTMLElement} */ (root.querySelector('#mcps-body'));
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

  const filtered = filterItems(items, search);

  if (filtered.length === 0) {
    const empty = items.length === 0;
    body.innerHTML = `
      <efs-card padded="false">
        <efs-empty-state
            title="${empty ? 'Nenhum MCP cadastrado' : 'Nada bate com a busca'}"
            description="${empty
              ? 'Cadastre um servidor MCP pra que seus agentes possam invocar suas tools em runtime.'
              : 'Tente ajustar o termo de busca.'}">
          <div data-efs-icon>${ServerIcon('h-6 w-6')}</div>
          ${empty
            ? `<div data-efs-action>
                 <a href="/mcps/novo"
                    class="inline-flex items-center gap-2 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-contrast hover:bg-accent-soft">
                   ${PlusIcon('h-4 w-4')}
                   <span>Cadastrar MCP</span>
                 </a>
               </div>`
            : ''}
        </efs-empty-state>
      </efs-card>
    `;
    return;
  }

  body.innerHTML = `
    <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
      ${filtered.map(mcpCardHtml).join('')}
    </div>
  `;

  body.querySelectorAll('[data-mcp-id]').forEach((card) => {
    const id = card.getAttribute('data-mcp-id');
    if (!id) return;
    /** @param {Event | KeyboardEvent} e */
    const goToEditor = (e) => {
      if ('key' in e && e.key !== 'Enter' && e.key !== ' ') return;
      e.preventDefault();
      window.location.assign(`/mcps/${encodeURIComponent(id)}`);
    };
    card.addEventListener('click', goToEditor);
    card.addEventListener('keydown', goToEditor);
  });
}

/**
 * @param {McpServer[]} list
 * @param {string} q
 */
function filterItems(list, q) {
  const trimmed = q.trim().toLowerCase();
  if (!trimmed) return list;
  return list.filter((m) =>
    m.name.toLowerCase().includes(trimmed)
    || m.serverLabel.toLowerCase().includes(trimmed)
    || m.serverUrl.toLowerCase().includes(trimmed)
    || (m.description ?? '').toLowerCase().includes(trimmed),
  );
}

/** @param {McpServer} m */
function mcpCardHtml(m) {
  const approvalTone = m.requireApproval === 'always'
    ? 'border-warning/40 bg-warning/10 text-warning'
    : 'border-success/40 bg-success/10 text-success';
  const approvalLabel = m.requireApproval === 'always' ? 'aprovação humana' : 'sem HITL';
  const toolsLabel = `${m.allowedTools.length} ${m.allowedTools.length === 1 ? 'tool' : 'tools'}`;

  return `
    <efs-card interactive>
      <div data-mcp-id="${escapeAttr(m.id)}" role="button" tabindex="0"
           class="group flex cursor-pointer flex-col items-start gap-3">
        <div class="flex w-full items-start justify-between gap-3">
          <div class="min-w-0 flex-1">
            <h3 class="truncate text-sm font-semibold text-fg">${escapeHtml(m.name)}</h3>
            <p class="mt-1 line-clamp-2 text-xs text-fg-muted">
              ${m.description ? escapeHtml(m.description) : '<span class="italic text-fg-dim">sem descrição</span>'}
            </p>
          </div>
          <span class="inline-flex shrink-0 items-center rounded-full border border-accent/40 bg-accent-subtle px-2 py-0.5 font-mono text-[10px] text-accent">${escapeHtml(m.serverLabel)}</span>
        </div>
        <code class="w-full truncate rounded-md bg-bg-soft px-2 py-1.5 font-mono text-[11px] text-fg-muted">${escapeHtml(m.serverUrl)}</code>
        <div class="mt-auto flex w-full items-center justify-between text-[11px] text-fg-dim">
          <div class="flex items-center gap-2">
            <span class="inline-flex items-center rounded-full border border-border bg-surface px-2 py-0.5 text-[10px] text-fg-muted">${toolsLabel}</span>
            <span class="inline-flex items-center rounded-full border ${approvalTone} px-2 py-0.5 text-[10px] font-medium">${approvalLabel}</span>
          </div>
          <span class="opacity-0 transition group-hover:opacity-100">Editar →</span>
        </div>
      </div>
    </efs-card>
  `;
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
