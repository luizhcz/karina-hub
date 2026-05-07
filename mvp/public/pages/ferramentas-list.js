// @ts-check
/**
 * Página /ferramentas — lista generic tools do projeto. Substitui
 * mvp/src/routes/ToolsList.tsx.
 *
 * Render imperativo: 1 fetch no load, busca client-side via input.
 * Nav `Nova ferramenta` aponta pra /ferramentas/nova (ainda React até Fase 2).
 */

import { listGenericTools } from '../lib/generic-tools.js';
import { friendlyError } from '../lib/api.js';
import { PlusIcon, SearchIcon, ToolIcon } from '../lib/icons.js';

/** @typedef {import('../lib/generic-tools.js').GenericTool} GenericTool */

const root = /** @type {HTMLElement} */ (document.getElementById('ferramentas-page'));

/** @type {GenericTool[]} */
let tools = [];
let loading = true;
/** @type {string | null} */
let error = null;
let search = '';

renderShell();
loadTools();

async function loadTools() {
  loading = true;
  renderBody();
  try {
    tools = await listGenericTools();
    error = null;
  } catch (err) {
    error = friendlyError(err, 'Não foi possível carregar as ferramentas.');
    tools = [];
  } finally {
    loading = false;
    renderBody();
  }
}

function renderShell() {
  root.innerHTML = `
    <div class="mb-8 flex items-end justify-between">
      <div>
        <h1 class="text-[28px] font-semibold tracking-tight text-fg">Ferramentas</h1>
        <p class="mt-2 text-sm text-fg-muted">Endpoints HTTP que seus agentes podem chamar. Crie uma vez e reutilize em qualquer fluxo.</p>
      </div>
      <a href="/ferramentas/nova"
         class="inline-flex items-center gap-2 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-contrast hover:bg-accent-soft">
        ${PlusIcon('h-4 w-4')}
        <span>Nova ferramenta</span>
      </a>
    </div>

    <div class="mb-6 max-w-md">
      <div class="relative">
        <span class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">
          ${SearchIcon('h-4 w-4')}
        </span>
        <input id="search-input" type="text"
               placeholder="Buscar por nome, descrição ou URL…"
               class="w-full rounded-lg border border-border bg-surface pl-10 pr-3 py-2 text-sm text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
      </div>
    </div>

    <div id="tools-body"></div>
  `;

  /** @type {HTMLInputElement | null} */
  const searchInput = root.querySelector('#search-input');
  searchInput?.addEventListener('input', () => {
    search = searchInput.value;
    renderBody();
  });
}

function renderBody() {
  const body = /** @type {HTMLElement} */ (root.querySelector('#tools-body'));
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

  const filtered = filterTools(tools, search);

  if (filtered.length === 0) {
    const empty = tools.length === 0;
    body.innerHTML = `
      <efs-card padded="false">
        <efs-empty-state
            title="${empty ? 'Nenhuma ferramenta ainda' : 'Nada bate com a busca'}"
            description="${empty
              ? 'Crie sua primeira ferramenta apontando para um endpoint HTTP que seu agente vai consumir.'
              : 'Tente ajustar o termo de busca.'}">
          <div data-efs-icon>${ToolIcon('h-6 w-6')}</div>
          ${empty
            ? `<div data-efs-action>
                 <a href="/ferramentas/nova"
                    class="inline-flex items-center gap-2 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-contrast hover:bg-accent-soft">
                   ${PlusIcon('h-4 w-4')}
                   <span>Criar ferramenta</span>
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
      ${filtered.map(toolCardHtml).join('')}
    </div>
  `;

  body.querySelectorAll('[data-tool-id]').forEach((card) => {
    const id = card.getAttribute('data-tool-id');
    if (!id) return;
    /** @param {Event | KeyboardEvent} e */
    const goToEditor = (e) => {
      if ('key' in e && e.key !== 'Enter' && e.key !== ' ') return;
      e.preventDefault();
      window.location.assign(`/ferramentas/${encodeURIComponent(id)}`);
    };
    card.addEventListener('click', goToEditor);
    card.addEventListener('keydown', goToEditor);
  });
}

/**
 * @param {GenericTool[]} list
 * @param {string} q
 */
function filterTools(list, q) {
  const trimmed = q.trim().toLowerCase();
  if (!trimmed) return list;
  return list.filter((t) =>
    t.name.toLowerCase().includes(trimmed)
    || t.description.toLowerCase().includes(trimmed)
    || t.urlTemplate.toLowerCase().includes(trimmed),
  );
}

/** @param {GenericTool} t */
function toolCardHtml(t) {
  const methodTone = t.httpMethod === 'GET' ? 'bg-success/10 text-success' : 'bg-warning/10 text-warning';
  return `
    <efs-card interactive>
      <div data-tool-id="${escapeAttr(t.id)}" role="button" tabindex="0"
           class="group flex cursor-pointer flex-col items-start gap-3">
        <div class="flex w-full items-start justify-between gap-3">
          <div class="min-w-0 flex-1">
            <h3 class="truncate text-sm font-semibold text-fg">${escapeHtml(t.name)}</h3>
            <p class="mt-1 line-clamp-2 text-xs text-fg-muted">
              ${t.description ? escapeHtml(t.description) : '<span class="italic text-fg-dim">sem descrição</span>'}
            </p>
          </div>
          <span class="inline-flex shrink-0 items-center rounded-full px-2 py-0.5 text-[10px] font-medium ${methodTone}">${escapeHtml(t.httpMethod)}</span>
        </div>
        <code class="w-full truncate rounded-md bg-bg-soft px-2 py-1.5 font-mono text-[11px] text-fg-muted">${escapeHtml(t.urlTemplate)}</code>
        <div class="mt-auto flex w-full items-center justify-between text-[11px] text-fg-dim">
          <div class="flex items-center gap-2">
            <span class="inline-flex items-center rounded-full border border-border bg-surface px-2 py-0.5 text-[10px] text-fg-muted">in: ${escapeHtml(t.inputContentType)}</span>
            <span class="inline-flex items-center rounded-full border border-border bg-surface px-2 py-0.5 text-[10px] text-fg-muted">out: ${escapeHtml(t.outputContentType)}</span>
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
