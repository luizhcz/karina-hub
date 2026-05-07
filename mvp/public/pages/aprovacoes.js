// @ts-check
/**
 * Página /aprovacoes — fila de drafts pendentes/rejeitados + modal review
 * com histórico, payload preview, approve/reject. Substitui
 * mvp/src/routes/Aprovacoes.tsx.
 */

import {
  approveAgentDraft,
  getDraftApprovalHistory,
  listAgentApprovals,
  rejectAgentDraft,
} from '../lib/agent-approvals.js';
import { ApiError, friendlyError } from '../lib/api.js';
import { AgentIcon, CheckIcon, SearchIcon } from '../lib/icons.js';
import { badge, button, errorMessage, textareaField } from '../lib/ui.js';

/** @typedef {import('../lib/agent-approvals.js').AgentDraft} AgentDraft */
/** @typedef {import('../lib/agent-approvals.js').DraftApprovalHistoryEntry} DraftApprovalHistoryEntry */

const root = /** @type {HTMLElement} */ (document.getElementById('aprovacoes-page'));

/** @type {'pending' | 'rejected'} */
let activeTab = 'pending';
/** @type {AgentDraft[]} */
let drafts = [];
let loading = true;
/** @type {string | null} */
let listError = null;
let search = '';

/** @type {AgentDraft | null} */
let selectedDraft = null;
/** @type {DraftApprovalHistoryEntry[]} */
let history = [];
let historyLoading = false;
let approving = false;
let rejecting = false;
let rejectMode = false;
let changeReason = '';
let feedback = '';
/** @type {string | null} */
let approveError = null;
/** @type {string | null} */
let rejectError = null;

/** @type {HTMLElement | null} */
let modalEl = null;

renderShell();
loadDrafts();

async function loadDrafts() {
  loading = true;
  listError = null;
  renderBody();
  try {
    drafts = await listAgentApprovals(activeTab);
  } catch (err) {
    drafts = [];
    if (err instanceof ApiError && err.status === 403) {
      listError = 'Esta tela é restrita a administradores. Se você precisa revisar agentes, fale com o time de governança.';
    } else {
      listError = friendlyError(err, 'Não foi possível carregar a fila de aprovações.');
    }
  } finally {
    loading = false;
    renderBody();
  }
}

function renderShell() {
  root.innerHTML = `
    <div class="mb-8">
      <h1 class="text-[28px] font-semibold tracking-tight text-fg">Aprovações</h1>
      <p class="mt-2 text-sm text-fg-muted">
        Revisão de rascunhos submetidos pelos times. Aprovar promove a uma versão publicada do agente; rejeitar devolve com feedback.
      </p>
    </div>

    <div class="mb-6 flex items-center gap-1 border-b border-border" id="tabs"></div>

    <div class="mb-6 max-w-md">
      <div class="relative">
        <span class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">
          ${SearchIcon('h-4 w-4')}
        </span>
        <input id="search-input" type="text" placeholder="Buscar por nome, descrição ou id…"
               class="w-full rounded-lg border border-border bg-surface pl-10 pr-3 py-2 text-sm text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20" />
      </div>
    </div>

    <div id="aprovacoes-body"></div>
  `;

  renderTabs();
  /** @type {HTMLInputElement | null} */
  const searchInput = root.querySelector('#search-input');
  searchInput?.addEventListener('input', () => {
    search = searchInput.value;
    renderBody();
  });
}

function renderTabs() {
  const tabsEl = /** @type {HTMLElement} */ (root.querySelector('#tabs'));
  tabsEl.innerHTML = [
    ['pending', 'Pendentes'],
    ['rejected', 'Rejeitados'],
  ].map(([key, label]) => {
    const isActive = activeTab === key;
    const stateClasses = isActive ? 'text-fg' : 'text-fg-muted hover:text-fg';
    return `
      <button type="button" data-tab="${key}"
              class="relative flex items-center gap-2 px-4 py-2.5 text-sm font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30 ${stateClasses}">
        ${escapeHtml(label)}
        ${isActive ? '<span class="absolute inset-x-0 bottom-0 h-0.5 bg-accent" aria-hidden="true"></span>' : ''}
      </button>
    `;
  }).join('');

  tabsEl.querySelectorAll('[data-tab]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const key = /** @type {'pending' | 'rejected'} */ (btn.getAttribute('data-tab'));
      if (key === activeTab) return;
      activeTab = key;
      renderTabs();
      loadDrafts();
    });
  });
}

function renderBody() {
  const body = /** @type {HTMLElement} */ (root.querySelector('#aprovacoes-body'));
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

  if (listError) {
    body.innerHTML = errorMessage(listError);
    return;
  }

  const filtered = filterDrafts(drafts, search);

  if (filtered.length === 0) {
    const empty = drafts.length === 0;
    const msg = empty
      ? activeTab === 'pending'
        ? 'Nenhum rascunho aguardando aprovação.'
        : 'Nenhum rascunho rejeitado.'
      : 'Nada bate com a busca. Tente ajustar o termo.';
    body.innerHTML = `<efs-card><p class="text-center text-sm text-fg-muted">${escapeHtml(msg)}</p></efs-card>`;
    return;
  }

  body.innerHTML = `
    <div class="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
      ${filtered.map(draftCardHtml).join('')}
    </div>
  `;

  body.querySelectorAll('[data-draft-id]').forEach((card) => {
    const id = card.getAttribute('data-draft-id');
    if (!id) return;
    const draft = drafts.find((d) => d.id === id);
    if (!draft) return;
    /** @param {Event | KeyboardEvent} e */
    const open = (e) => {
      if ('key' in e && e.key !== 'Enter' && e.key !== ' ') return;
      e.preventDefault();
      openModal(draft);
    };
    card.addEventListener('click', open);
    card.addEventListener('keydown', open);
  });
}

/**
 * @param {AgentDraft[]} list
 * @param {string} q
 */
function filterDrafts(list, q) {
  const trimmed = q.trim().toLowerCase();
  if (!trimmed) return list;
  return list.filter((d) => {
    const name = (d.name || /** @type {string} */ (d.payload?.name) || '').toLowerCase();
    const desc = ((/** @type {string} */ (d.payload?.description)) ?? '').toLowerCase();
    return name.includes(trimmed) || desc.includes(trimmed) || d.id.toLowerCase().includes(trimmed);
  });
}

/** @param {AgentDraft} draft */
function draftCardHtml(draft) {
  const display = draft.name || /** @type {string} */ (draft.payload?.name) || 'Rascunho sem nome';
  const description = /** @type {string} */ (draft.payload?.description) ?? '';
  const submittedAt = draft.submittedAt ?? draft.updatedAt;
  const stripeColor = draft.status === 'Rejected' ? 'before:bg-warning' : 'before:bg-accent';
  const statusBadge = draft.status === 'Rejected'
    ? badge('Rejeitado', { tone: 'warning' })
    : badge('Aguardando', { tone: 'accent' });

  return `
    <efs-card interactive padded="false">
      <div data-draft-id="${escapeAttr(draft.id)}" role="button" tabindex="0"
           class="group relative flex min-h-[180px] cursor-pointer flex-col gap-3 overflow-hidden p-5 before:absolute before:inset-y-0 before:left-0 before:w-1 ${stripeColor}">
        <div class="flex items-start justify-between gap-3">
          <div class="flex min-w-0 items-center gap-3">
            <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
              ${AgentIcon('h-5 w-5')}
            </div>
            <h3 class="min-w-0 truncate text-sm font-semibold text-fg">${escapeHtml(display)}</h3>
          </div>
          ${statusBadge}
        </div>
        <p class="line-clamp-3 text-xs text-fg-muted">
          ${description ? escapeHtml(description) : '<span class="italic text-fg-dim">sem descrição</span>'}
        </p>
        <div class="mt-auto flex items-center justify-between text-[11px] text-fg-dim">
          <div class="flex items-center gap-2">
            ${draft.isEditDraft ? badge('edição') : ''}
            <span>submetido ${escapeHtml(formatRelative(submittedAt))}</span>
          </div>
          <span class="opacity-0 transition group-hover:opacity-100">Revisar →</span>
        </div>
      </div>
    </efs-card>
  `;
}

// ── Modal de review ───────────────────────────────────────────────────────

/** @param {AgentDraft} draft */
function openModal(draft) {
  selectedDraft = draft;
  history = [];
  historyLoading = true;
  approving = false;
  rejecting = false;
  rejectMode = false;
  changeReason = '';
  feedback = '';
  approveError = null;
  rejectError = null;

  if (!modalEl) {
    modalEl = document.createElement('efs-modal');
    modalEl.setAttribute('size', 'lg');
    modalEl.addEventListener('close', () => {
      if (approving || rejecting) return;
      closeModal();
    });
    document.body.appendChild(modalEl);
  }

  const title = draft.name || /** @type {string} */ (draft.payload?.name) || draft.id;
  const description = draft.isEditDraft
    ? `Edição do agente ${draft.baseAgentId} (rev. ${draft.baseRevision ?? '?'})`
    : 'Novo agente';
  modalEl.setAttribute('title', title);
  modalEl.setAttribute('description', description);
  modalEl.setAttribute('open', '');

  renderModal();

  // Carrega histórico
  getDraftApprovalHistory(draft.id)
    .then((entries) => {
      if (selectedDraft?.id !== draft.id) return;
      history = entries;
    })
    .catch(() => {
      if (selectedDraft?.id !== draft.id) return;
      history = [];
    })
    .finally(() => {
      if (selectedDraft?.id !== draft.id) return;
      historyLoading = false;
      renderModal();
    });
}

function closeModal() {
  modalEl?.removeAttribute('open');
  selectedDraft = null;
}

function renderModal() {
  if (!modalEl || !selectedDraft) return;

  const draft = selectedDraft;
  const isPending = draft.status === 'PendingApproval';
  const isRejected = draft.status === 'Rejected';
  const busy = approving || rejecting;

  const headerBadges = [
    isRejected ? badge('Rejeitado', { tone: 'warning' }) : badge('Aguardando aprovação', { tone: 'accent' }),
    draft.isEditDraft ? badge('edição') : '',
    draft.createdBy ? `<span class="text-[11px] text-fg-dim">autor: <span class="font-mono text-fg">${escapeHtml(draft.createdBy)}</span></span>` : '',
    draft.submittedAt ? `<span class="text-[11px] text-fg-dim">submetido em ${escapeHtml(formatAbsolute(draft.submittedAt))}</span>` : '',
  ].filter(Boolean).join('');

  const historyHtml = historyLoading
    ? '<div class="flex items-center justify-center py-4"><efs-spinner class="inline-flex h-5 w-5 text-fg-muted"></efs-spinner></div>'
    : history.length === 0
      ? '<p class="text-xs text-fg-muted">Sem eventos registrados.</p>'
      : `<ol class="relative space-y-2 border-l border-border pl-4">
          ${history.map((e) => `
            <li class="text-xs">
              <div class="flex flex-wrap items-center gap-2">
                <span class="font-semibold text-fg">${escapeHtml(e.action)}</span>
                ${e.tier ? badge(e.tier, { tone: e.tier === 'Cosmetic' ? 'success' : 'accent' }) : ''}
                <span class="text-fg-dim">${escapeHtml(formatAbsolute(e.occurredAt))}</span>
              </div>
              <p class="mt-0.5 text-fg-muted">por <span class="font-mono text-fg">${escapeHtml(e.actorUserId)}</span></p>
              ${e.feedback ? `<p class="mt-1 whitespace-pre-wrap rounded-md border border-border bg-bg-soft px-2 py-1 text-fg">${escapeHtml(e.feedback)}</p>` : ''}
            </li>
          `).join('')}
        </ol>`;

  const approveActions = isPending && !rejectMode ? `
    <div class="flex flex-wrap items-center justify-end gap-2">
      ${button({ label: 'Fechar', variant: 'ghost', disabled: busy, attrs: { 'data-action': 'close' } })}
      ${button({ label: 'Rejeitar', variant: 'danger', disabled: busy, attrs: { 'data-action': 'reject-mode' } })}
      ${button({ label: 'Aprovar', loading: approving, leftIcon: CheckIcon('h-4 w-4'), attrs: { 'data-action': 'approve' } })}
    </div>
  ` : '';

  const rejectActions = isPending && rejectMode ? `
    <div class="flex flex-wrap items-center justify-end gap-2">
      ${button({ label: 'Voltar', variant: 'ghost', disabled: busy, attrs: { 'data-action': 'cancel-reject' } })}
      ${button({ label: 'Confirmar rejeição', variant: 'danger', loading: rejecting, attrs: { 'data-action': 'reject' } })}
    </div>
  ` : '';

  const closeActions = isRejected ? `
    <div class="flex justify-end">
      ${button({ label: 'Fechar', variant: 'ghost', attrs: { 'data-action': 'close' } })}
    </div>
  ` : '';

  modalEl.innerHTML = `
    <div class="space-y-5">
      <div class="flex flex-wrap items-center gap-2">${headerBadges}</div>
      ${payloadPreviewHtml(draft)}
      ${
        isRejected && draft.rejectionFeedback
          ? `<efs-card>
               <h3 class="text-sm font-semibold text-fg">Feedback de rejeição anterior</h3>
               <p class="mt-2 whitespace-pre-wrap text-xs text-fg">${escapeHtml(draft.rejectionFeedback)}</p>
             </efs-card>`
          : ''
      }
      <efs-card>
        <h3 class="text-sm font-semibold text-fg mb-2">Histórico do rascunho</h3>
        ${historyHtml}
      </efs-card>
      ${
        isPending && !rejectMode
          ? `<div class="space-y-2">
               ${textareaField({
                 label: 'Motivo da aprovação (opcional)',
                 name: 'changeReason',
                 value: changeReason,
                 placeholder: 'Ex.: validado com a área de risco, sem impacto comportamental…',
                 rows: 2,
                 disabled: busy,
               })}
               <p class="text-[11px] text-fg-dim">Fica registrado no histórico junto com o seu nome de admin.</p>
             </div>`
          : ''
      }
      ${
        isPending && rejectMode
          ? `<div class="space-y-2">
               ${textareaField({
                 label: 'Feedback obrigatório (mínimo 10 caracteres)',
                 name: 'feedback',
                 value: feedback,
                 placeholder: 'Explique o que precisa ser ajustado pra que o time autor possa corrigir…',
                 rows: 3,
                 disabled: busy,
               })}
               <div class="flex items-center justify-between text-[11px] text-fg-dim">
                 <span>${feedback.trim().length}/10 caracteres mínimos</span>
               </div>
             </div>`
          : ''
      }
      ${approveError ? errorMessage(approveError) : ''}
      ${rejectError ? errorMessage(rejectError) : ''}
      ${approveActions}
      ${rejectActions}
      ${closeActions}
    </div>
  `;

  // Wire textareas
  const reasonArea = /** @type {HTMLTextAreaElement | null} */ (modalEl.querySelector('[name="changeReason"]'));
  reasonArea?.addEventListener('input', () => { changeReason = reasonArea.value; });
  const feedbackArea = /** @type {HTMLTextAreaElement | null} */ (modalEl.querySelector('[name="feedback"]'));
  feedbackArea?.addEventListener('input', () => {
    feedback = feedbackArea.value;
    // Atualiza contador
    const counter = modalEl?.querySelector('.flex.items-center.justify-between.text-fg-dim span');
    if (counter) counter.textContent = `${feedback.trim().length}/10 caracteres mínimos`;
  });

  // Wire actions
  modalEl.querySelectorAll('[data-action]').forEach((btn) => {
    const action = btn.getAttribute('data-action');
    btn.addEventListener('click', () => onAction(action));
  });
}

/** @param {string | null} action */
async function onAction(action) {
  if (!selectedDraft) return;
  switch (action) {
    case 'close':
      closeModal();
      return;
    case 'reject-mode':
      rejectMode = true;
      renderModal();
      return;
    case 'cancel-reject':
      rejectMode = false;
      rejectError = null;
      renderModal();
      return;
    case 'approve':
      await doApprove();
      return;
    case 'reject':
      await doReject();
      return;
  }
}

async function doApprove() {
  if (!selectedDraft) return;
  approving = true;
  approveError = null;
  renderModal();
  try {
    await approveAgentDraft(selectedDraft.id, { changeReason: changeReason.trim() || null });
    closeModal();
    await loadDrafts();
  } catch (err) {
    approveError = friendlyError(err, 'Não foi possível aprovar este rascunho.');
  } finally {
    approving = false;
    if (selectedDraft) renderModal();
  }
}

async function doReject() {
  if (!selectedDraft) return;
  if (feedback.trim().length < 10) {
    rejectError = 'O feedback é obrigatório e precisa ter pelo menos 10 caracteres.';
    renderModal();
    return;
  }
  rejecting = true;
  rejectError = null;
  renderModal();
  try {
    await rejectAgentDraft(selectedDraft.id, { feedback: feedback.trim() });
    closeModal();
    await loadDrafts();
  } catch (err) {
    rejectError = friendlyError(err, 'Não foi possível rejeitar este rascunho.');
  } finally {
    rejecting = false;
    if (selectedDraft) renderModal();
  }
}

/** @param {AgentDraft} draft */
function payloadPreviewHtml(draft) {
  const p = draft.payload || {};
  const model = /** @type {{ deploymentName?: string, predefinedModelId?: string | null } | undefined} */ (p.model);
  /** @type {Array<{type: string, name?: string | null, genericToolId?: string | null, mcpServerId?: string | null}>} */
  const tools = /** @type {any} */ (p.tools) ?? [];
  const visibility = /** @type {string} */ (p.visibility) ?? '—';
  const enabled = /** @type {boolean} */ (p.enabled);
  const instructions = /** @type {string} */ (p.instructions) ?? '';

  const fieldHtml = (label, value) => `
    <div>
      <dt class="text-[11px] uppercase tracking-wider text-fg-dim">${escapeHtml(label)}</dt>
      <dd class="mt-0.5 text-sm text-fg">${value}</dd>
    </div>
  `;

  return `
    <efs-card>
      <div class="space-y-3">
        <div>
          <h3 class="text-sm font-semibold text-fg">Conteúdo do rascunho</h3>
          <p class="mt-1 text-xs text-fg-muted">Snapshot do payload enviado pelo time autor.</p>
        </div>
        <dl class="grid grid-cols-1 gap-3 sm:grid-cols-2">
          ${fieldHtml('Nome', escapeHtml(/** @type {string} */ (p.name) || draft.name || '—'))}
          ${fieldHtml('Visibilidade', escapeHtml(visibility))}
          ${fieldHtml('Modelo', model?.predefinedModelId || model?.deploymentName || '<span class="italic text-fg-dim">não definido</span>')}
          ${fieldHtml('Habilitado ao publicar', enabled === false ? 'não' : 'sim')}
          ${fieldHtml('Ferramentas', tools.length === 0 ? '<span class="italic text-fg-dim">nenhuma</span>' : `${tools.length} ferramenta${tools.length === 1 ? '' : 's'}`)}
          ${fieldHtml('Descrição', /** @type {string} */ (p.description) ? escapeHtml(/** @type {string} */ (p.description)) : '<span class="italic text-fg-dim">sem descrição</span>')}
        </dl>
        ${tools.length > 0 ? `
          <div>
            <p class="text-[11px] uppercase tracking-wider text-fg-dim">Tools referenciadas</p>
            <ul class="mt-1 space-y-1">
              ${tools.map((t) => `
                <li class="font-mono text-[11px] text-fg-muted">
                  <span class="font-semibold text-fg">${escapeHtml(t.type)}</span>
                  ${t.genericToolId ? ` · ${escapeHtml(t.genericToolId)}` : t.mcpServerId ? ` · ${escapeHtml(t.mcpServerId)}` : t.name ? ` · ${escapeHtml(t.name)}` : ''}
                </li>
              `).join('')}
            </ul>
          </div>
        ` : ''}
        <div>
          <p class="text-[11px] uppercase tracking-wider text-fg-dim">Instruções</p>
          <pre class="mt-1 max-h-64 overflow-y-auto whitespace-pre-wrap rounded-md border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] leading-relaxed text-fg">${
            instructions ? escapeHtml(instructions) : '<span class="italic text-fg-dim">sem instruções</span>'
          }</pre>
        </div>
      </div>
    </efs-card>
  `;
}

// ── Helpers ───────────────────────────────────────────────────────────────

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

/** @param {string | null | undefined} iso */
function formatRelative(iso) {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const diffMs = Date.now() - date.getTime();
  const minutes = Math.round(diffMs / 60_000);
  if (minutes < 1) return 'agora';
  if (minutes < 60) return `há ${minutes} min`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `há ${hours} h`;
  const days = Math.round(hours / 24);
  if (days < 7) return `há ${days} d`;
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
