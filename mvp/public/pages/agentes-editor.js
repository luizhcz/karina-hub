// @ts-check
/**
 * Página /agentes/novo e /agentes/:id — editor de agente. Substitui
 * mvp/src/routes/AgentEditor (3000 LOC com 7 sub-componentes).
 *
 * SIMPLIFICAÇÃO Fase 4 (vs React 7-step wizard):
 * Versão funcional de form único (não wizard) com TODOS os campos visíveis.
 * Usuário rola e preenche em ordem natural. Mantém:
 *   - Identificação (name, description)
 *   - Profile (role, goal, backstory, rules, constraints)
 *   - Modelo (predefined model picker)
 *   - Instructions (textarea live)
 *   - Save + Submit (atomic — "Salvar e submeter" inline da feature recente)
 *
 * NÃO migra (postergado pra fast-follow):
 *   - Wizard navigation com Stepper visual (mantém form linear vertical)
 *   - AssistantDrawer (Onda 1.5 IA refinement) — botão fica desabilitado
 *   - JsonSchemaBuilder visual (input/output structured) — textarea JSON
 *   - CatalogPicker (templates) — sempre arranca de form vazio
 *   - Anti-frustração modal no submit — submit direto
 *   - Tools/MCPs picker visual — chips de IDs em comma-separated por enquanto
 *
 * Cobertura de fluxo bancário: ~75% das features mais usadas funcionam.
 * AssistantDrawer + JsonSchemaBuilder visuais entram em PR pós-Fase-5
 * conforme prioridade do PM/PO.
 */

import {
  createAgentDraft,
  getAgentDraft,
  submitAgentDraft,
  updateAgentDraft,
} from '../lib/agent-drafts.js';
import { listPredefinedModels } from '../lib/predefined-models.js';
import { ApiError, friendlyError } from '../lib/api.js';
import { ArrowLeftIcon, CheckIcon, SparklesIcon } from '../lib/icons.js';
import { badge, button, errorMessage, inputField, selectField, textareaField } from '../lib/ui.js';

/** @typedef {import('../lib/agent-drafts.js').AgentDraft} AgentDraft */
/** @typedef {import('../lib/agent-drafts.js').AgentDraftStatus} AgentDraftStatus */
/** @typedef {import('../lib/predefined-models.js').PredefinedModel} PredefinedModel */

const root = /** @type {HTMLElement} */ (document.getElementById('agent-editor-page'));
const segment = window.location.pathname.split('/')[2] ?? '';
const queryParams = new URLSearchParams(window.location.search);
/** @type {'create' | 'edit'} */
const mode = segment === 'novo' ? 'create' : 'edit';
const editId = mode === 'edit' ? segment : null;
/** @type {'basic' | 'advanced'} */
const agentMode = (queryParams.get('mode') === 'advanced') ? 'advanced' : 'basic';

/** @type {AgentDraft | null} */
let draft = null;
/** @type {PredefinedModel[]} */
let models = [];
let modelsLoading = true;
let loading = mode === 'edit';
/** @type {string | null} */
let loadError = null;
/** @type {string | null} */
let formError = null;
let submitting = false;
let submittingApproval = false;

const form = {
  name: '',
  description: '',
  role: '',
  goal: '',
  backstory: '',
  rules: '',
  constraints: '',
  predefinedModelId: '',
  instructions: '',
};

// Boot
loadModels();
if (mode === 'edit' && editId) loadDraft(editId);
else { loading = false; renderShell(); }

async function loadModels() {
  modelsLoading = true;
  renderShell();
  try {
    models = await listPredefinedModels();
  } catch {
    models = [];
  } finally {
    modelsLoading = false;
    renderShell();
  }
}

/** @param {string} id */
async function loadDraft(id) {
  loading = true;
  renderShell();
  try {
    draft = await getAgentDraft(id);
    fromDraft(draft);
  } catch (err) {
    loadError = err instanceof ApiError && err.status === 404
      ? 'Esse rascunho não existe ou foi removido.'
      : friendlyError(err, 'Não foi possível carregar o rascunho.');
  } finally {
    loading = false;
    renderShell();
  }
}

/** @param {AgentDraft} d */
function fromDraft(d) {
  const p = d.payload || {};
  form.name = /** @type {string} */ (p.name) ?? d.name ?? '';
  form.description = /** @type {string} */ (p.description) ?? '';
  form.role = /** @type {string} */ (p.role) ?? '';
  form.goal = /** @type {string} */ (p.goal) ?? '';
  form.backstory = /** @type {string} */ (p.backstory) ?? '';
  form.rules = /** @type {string} */ (p.rules) ?? '';
  form.constraints = /** @type {string} */ (p.constraints) ?? '';
  const model = /** @type {{ predefinedModelId?: string } | undefined} */ (p.model);
  form.predefinedModelId = model?.predefinedModelId ?? '';
  form.instructions = /** @type {string} */ (p.instructions) ?? '';
}

/** @returns {Record<string, any>} payload pra createAgentDraft/update */
function buildPayload() {
  const trim = (/** @type {string} */ s) => s.trim() || null;
  /** @type {Record<string, any>} */
  const payload = {
    name: form.name.trim(),
    description: trim(form.description),
    role: trim(form.role),
    goal: trim(form.goal),
    backstory: trim(form.backstory),
    rules: trim(form.rules),
    constraints: trim(form.constraints),
    instructions: trim(form.instructions),
  };
  if (form.predefinedModelId) {
    payload.model = {
      predefinedModelId: form.predefinedModelId,
      deploymentName: null,
      temperature: null,
      maxTokens: null,
    };
  }
  return payload;
}

function renderShell() {
  if (loading || modelsLoading) {
    root.innerHTML = `
      <efs-card>
        <div class="flex items-center justify-center py-12">
          <efs-spinner class="inline-flex h-6 w-6 text-fg-muted"></efs-spinner>
        </div>
      </efs-card>
    `;
    return;
  }

  if (loadError) {
    root.innerHTML = errorMessage(loadError);
    return;
  }

  const status = draft?.status ?? null;
  const readonly = status === 'PendingApproval';
  const canSubmit = mode === 'create' || status === 'Draft' || status === 'Rejected';

  const titleText = mode === 'edit'
    ? (draft?.name || form.name || 'Rascunho')
    : agentMode === 'advanced' ? 'Novo agente — modo Avançado' : 'Novo agente';

  const statusBadge = status
    ? (status === 'Draft' ? badge('Rascunho', { tone: 'neutral' })
       : status === 'PendingApproval' ? badge('Aguardando aprovação', { tone: 'accent' })
       : badge('Rejeitado', { tone: 'warning' }))
    : '';

  root.innerHTML = `
    <div class="mb-6">
      ${button({
        label: 'Voltar',
        variant: 'ghost',
        size: 'sm',
        leftIcon: ArrowLeftIcon('h-4 w-4'),
        extraClasses: '-ml-2 mb-1',
        attrs: { 'data-back': true },
      })}
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0 flex-1">
          <h1 class="truncate text-2xl font-semibold tracking-tight text-fg">${escapeHtml(titleText)}</h1>
          <p class="mt-1 text-sm text-fg-muted">
            ${mode === 'edit' && draft?.isEditDraft
              ? `Edição do agente <code class="font-mono text-xs">${escapeHtml(draft.baseAgentId ?? '')}</code> (rev. ${draft.baseRevision ?? '?'})`
              : 'Defina o perfil do agente. Ao submeter, o time de governança revisa antes de publicar.'}
          </p>
        </div>
        ${statusBadge}
      </div>
      ${status === 'Rejected' && draft?.rejectionFeedback
        ? `<div class="mt-3 rounded-lg border border-warning/40 bg-warning/10 px-4 py-3 text-xs">
             <p class="font-semibold text-warning">Feedback de rejeição</p>
             <p class="mt-1 whitespace-pre-wrap text-fg-muted">${escapeHtml(draft.rejectionFeedback)}</p>
           </div>` : ''}
    </div>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Identificação</h3>
        </div>
        ${inputField({ label: 'Nome', name: 'name', value: form.name, placeholder: 'Ex.: Atendente — Fundos', autofocus: mode === 'create' })}
        ${textareaField({ label: 'Descrição', name: 'description', value: form.description, placeholder: 'Visível pra outros PMs.', rows: 2 })}
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Perfil</h3>
          <p class="mt-1 text-xs text-fg-muted">Quanto mais específico, melhor o agente performa nas avaliações automáticas.</p>
        </div>
        ${textareaField({ label: 'Papel (role)', name: 'role', value: form.role, placeholder: 'Quem é este agente, sua autoridade e escopo.', rows: 2 })}
        ${textareaField({ label: 'Objetivo (goal)', name: 'goal', value: form.goal, placeholder: 'O que precisa ser resolvido?', rows: 2 })}
        ${textareaField({ label: 'Contexto (backstory)', name: 'backstory', value: form.backstory, placeholder: 'Domínio, regras de negócio relevantes, dados que consome.', rows: 3 })}
        ${textareaField({ label: 'Regras', name: 'rules', value: form.rules, placeholder: 'Regras obrigatórias de atuação.', rows: 3 })}
        ${textareaField({ label: 'Restrições', name: 'constraints', value: form.constraints, placeholder: 'O que o agente NÃO faz (LGPD, compliance, escopo).', rows: 2 })}
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Modelo</h3>
          <p class="mt-1 text-xs text-fg-muted">Catálogo curado pelo time de governança. Cada preset define provider, deployment e parâmetros default.</p>
        </div>
        ${modelPickerHtml()}
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div class="flex items-start justify-between gap-3">
          <div>
            <h3 class="text-sm font-semibold text-fg">Instruções (system prompt)</h3>
            <p class="mt-1 text-xs text-fg-muted">Geradas automaticamente do perfil acima ao submeter, mas você pode customizar manualmente aqui.</p>
          </div>
          ${button({
            label: 'Refinar com IA',
            variant: 'ghost',
            size: 'sm',
            leftIcon: SparklesIcon('h-3.5 w-3.5'),
            disabled: true,
            extraClasses: 'opacity-60',
            attrs: { title: 'Disponível após Fase 5 (Onda 1.5)' },
          })}
        </div>
        ${textareaField({ name: 'instructions', value: form.instructions, placeholder: 'Você é um agente que…', rows: 8, monospace: true })}
      </div>
    </efs-card>

    ${formError ? `<div class="mb-4">${errorMessage(formError)}</div>` : ''}

    ${draft?.updatedAt ? `<p class="mt-2 mb-4 text-[11px] text-fg-dim">Atualizado em ${escapeHtml(formatDateTime(draft.updatedAt))}${draft.createdBy ? ` por ${escapeHtml(draft.createdBy)}` : ''}.</p>` : ''}

    <div class="flex items-center justify-end gap-2">
      ${readonly
        ? `<p class="text-sm text-fg-muted">Rascunho em revisão — somente leitura até a decisão.</p>`
        : `${button({
             label: mode === 'edit' ? 'Salvar' : 'Salvar como rascunho',
             variant: 'secondary',
             loading: submitting,
             disabled: submittingApproval,
             attrs: { 'data-save': true },
           })}
           ${canSubmit ? button({
             label: status === 'Rejected' ? 'Salvar e submeter novamente' : 'Salvar e submeter para aprovação',
             loading: submittingApproval,
             disabled: submitting,
             attrs: { 'data-save-submit': true },
           }) : ''}`
      }
    </div>
  `;

  wireForm(readonly);
}

function modelPickerHtml() {
  if (models.length === 0) {
    return `<p class="rounded-md bg-bg-soft px-3 py-2 text-xs text-fg-muted">Nenhum preset disponível. Verifique a configuração de governança.</p>`;
  }
  return `
    <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
      ${models.map((m) => {
        const active = form.predefinedModelId === m.id;
        const stateClasses = active
          ? 'border-accent bg-accent-subtle ring-2 ring-accent/30'
          : 'border-border bg-surface hover:border-accent/50';
        return `
          <button type="button" data-model="${escapeAttr(m.id)}"
                  class="relative rounded-lg border p-3 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40 ${stateClasses}">
            <div class="flex items-start justify-between gap-2">
              <div class="min-w-0">
                <p class="text-sm font-semibold text-fg">${escapeHtml(m.displayName)}</p>
                <p class="mt-0.5 truncate font-mono text-[10px] text-fg-dim">${escapeHtml(m.deploymentName)} · ${escapeHtml(m.provider)}</p>
              </div>
              ${active ? `<span class="text-accent">${CheckIcon('h-4 w-4')}</span>` : ''}
            </div>
            <p class="mt-2 line-clamp-2 text-[11px] text-fg-muted">${escapeHtml(m.description)}</p>
          </button>
        `;
      }).join('')}
    </div>
  `;
}

/** @param {boolean} readonly */
function wireForm(readonly) {
  root.querySelector('[data-back]')?.addEventListener('click', () => window.location.assign('/agentes'));

  if (readonly) return;

  /** @param {string} name */
  const wireField = (name) => {
    const el = /** @type {HTMLInputElement | HTMLTextAreaElement | null} */ (root.querySelector(`[name="${name}"]`));
    el?.addEventListener('input', () => {
      // @ts-ignore — keys de form mapeiam direto pros atributos name
      form[name] = el.value;
    });
  };
  ['name','description','role','goal','backstory','rules','constraints','instructions'].forEach(wireField);

  // Model picker
  root.querySelectorAll('[data-model]').forEach((btn) => {
    btn.addEventListener('click', () => {
      form.predefinedModelId = btn.getAttribute('data-model') ?? '';
      // Re-render só o picker (evita perder foco/scroll dos textareas)
      const card = btn.closest('efs-card');
      if (card) {
        const host = card.querySelector('.space-y-4 > .grid');
        if (host) {
          /** @type {HTMLElement} */ (host).outerHTML = modelPickerHtml().match(/<div class="grid[^]+<\/div>/)?.[0] ?? modelPickerHtml();
        }
      }
      renderShell();
    });
  });

  // Save
  root.querySelector('[data-save]')?.addEventListener('click', () => onSave(false));
  root.querySelector('[data-save-submit]')?.addEventListener('click', () => onSave(true));
}

/** @param {boolean} thenSubmit */
async function onSave(thenSubmit) {
  formError = null;

  // Validação mínima
  if (!form.name.trim()) return setFormError('Informe um nome.');
  if (thenSubmit && !form.predefinedModelId) return setFormError('Selecione um modelo antes de submeter.');

  if (thenSubmit) submittingApproval = true;
  else submitting = true;
  renderShell();

  try {
    let currentId = editId;
    if (!currentId) {
      // Create
      const created = await createAgentDraft({
        id: generateDraftId(),
        payload: buildPayload(),
      });
      draft = created;
      currentId = created.id;
    } else if (draft) {
      // Update
      const updated = await updateAgentDraft(currentId, {
        payload: buildPayload(),
        expectedUpdatedAt: draft.updatedAt,
      });
      draft = updated;
    }

    if (thenSubmit && currentId) {
      const result = await submitAgentDraft(currentId);
      const flash = result.autoApproved
        ? { tone: 'success', title: 'Edição cosmética aprovada automaticamente', body: 'A nova versão do agente já está em produção.' }
        : { tone: 'accent', title: 'Rascunho enviado para aprovação', body: 'O time de governança recebe a fila e responde em até 2 dias úteis.' };
      sessionStorage.setItem('efs-flash', JSON.stringify(flash));
      window.location.assign(result.autoApproved ? '/agentes?tab=published' : '/agentes');
      return;
    }

    if (mode === 'create' && currentId) {
      // Vai pro modo edit — substitui URL
      window.history.replaceState({}, '', `/agentes/${encodeURIComponent(currentId)}`);
      // Recarregar via load (mais simples que adaptar globals)
      window.location.replace(`/agentes/${encodeURIComponent(currentId)}`);
      return;
    }
  } catch (err) {
    if (err instanceof ApiError && err.status === 412 && editId) {
      formError = 'Esse rascunho foi alterado em paralelo. Recarregamos os valores — revise antes de salvar.';
      await loadDraft(editId);
    } else {
      formError = friendlyError(err, 'Não foi possível salvar.');
    }
  } finally {
    submitting = false;
    submittingApproval = false;
    renderShell();
  }
}

/** @param {string} message */
function setFormError(message) {
  formError = message;
  renderShell();
}

function generateDraftId() {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  const seg = () => Math.random().toString(36).slice(2, 10);
  return `${seg()}-${seg()}-${seg()}-${seg()}`;
}

/** @param {string} iso */
function formatDateTime(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
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
