// @ts-check
/**
 * Página /agentes/:id/implantar — orquestração de deploy do workflow.
 * Substitui mvp/src/routes/AgentDeploy.tsx.
 *
 * Avaliação automática REMOVIDA da tela: o usuário pode disparar avaliações
 * manualmente pela aba /avaliacoes se quiser. Aqui o foco é só o deploy.
 */

import { getAgent } from '../lib/agents.js';
import {
  createWorkflow,
  deploymentWorkflowId,
  getWorkflow,
  getWorkflowEnabledStatus,
} from '../lib/workflows.js';
import { getSystemInfo } from '../lib/system.js';
import { ApiError, friendlyError } from '../lib/api.js';
import { ArrowLeftIcon, BoltIcon } from '../lib/icons.js';
import { badge, button, errorMessage } from '../lib/ui.js';

/** @typedef {import('../lib/agents.js').Agent} Agent */
/** @typedef {import('../lib/workflows.js').Workflow} Workflow */
/** @typedef {import('../lib/workflows.js').WorkflowEnabledStatus} WorkflowEnabledStatus */

const root = /** @type {HTMLElement} */ (document.getElementById('deploy-page'));
const agentId = window.location.pathname.split('/')[2] ?? '';

/** @type {Agent | null} */
let agent = null;
/** @type {Workflow | null} */
let workflow = null;
/** @type {WorkflowEnabledStatus | null} */
let enabledStatus = null;
/** @type {string | null} */
let publicBaseUrl = null;
let loading = true;
/** @type {string | null} */
let loadError = null;
let deploying = false;
/** @type {string | null} */
let deployError = null;

if (!agentId) {
  root.innerHTML = errorMessage('ID do agente ausente na URL.');
} else {
  loadAll();
}

async function loadAll() {
  loading = true;
  loadError = null;
  renderShell();
  try {
    const [a, info] = await Promise.all([getAgent(agentId), getSystemInfo()]);
    agent = a;
    publicBaseUrl = info.publicBaseUrl;

    try {
      const wf = await getWorkflow(deploymentWorkflowId(agentId));
      workflow = wf;
      refreshEnabledStatus(wf.id).catch(() => { /* ignore */ });
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        // Esperado quando ainda não foi implantado.
      } else {
        loadError = friendlyError(err, 'Falha ao consultar implantação existente.');
      }
    }
  } catch (err) {
    loadError = friendlyError(err, 'Não foi possível carregar o agente.');
  } finally {
    loading = false;
    renderShell();
  }
}

/** @param {string} workflowId */
async function refreshEnabledStatus(workflowId) {
  try {
    enabledStatus = await getWorkflowEnabledStatus(workflowId);
    renderShell();
  } catch {
    enabledStatus = null;
  }
}

async function handleDeploy() {
  if (!agent || !agentId) return;
  deploying = true;
  deployError = null;
  renderShell();
  try {
    const wf = await createWorkflow({
      id: deploymentWorkflowId(agentId),
      name: agent.name,
      description: agent.description ?? null,
      version: '1.0.0',
      orchestrationMode: 'Graph',
      agents: [{ agentId, agentVersionId: null }],
      executors: [],
      edges: [],
      configuration: {
        maxRounds: 1,
        timeoutSeconds: 300,
        checkpointMode: 'InMemory',
        inputMode: 'Standalone',
        enableHumanInTheLoop: false,
        exposeAsAgent: false,
      },
      metadata: { deployedFromAgentId: agentId },
      visibility: 'project',
    });
    workflow = wf;
    refreshEnabledStatus(wf.id).catch(() => { /* ignore */ });
  } catch (err) {
    deployError = friendlyError(err, 'Não foi possível concluir a implantação.');
  } finally {
    deploying = false;
    renderShell();
  }
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

  if (loadError || !agent) {
    root.innerHTML = errorMessage(loadError ?? 'Agente não encontrado.');
    return;
  }

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
      <h1 class="text-2xl font-semibold tracking-tight text-fg">Implantar — ${escapeHtml(agent.name)}</h1>
      <p class="mt-1 text-sm text-fg-muted">
        Cria um workflow Graph que expõe o agente para consumo via API.
      </p>
    </div>

    ${workflow ? deployedViewHtml() : pendingViewHtml()}
  `;

  root.querySelector('[data-back]')?.addEventListener('click', () => window.location.assign('/agentes'));
  root.querySelector('[data-deploy]')?.addEventListener('click', handleDeploy);
}

function pendingViewHtml() {
  return `
    <div class="space-y-5">
      <efs-card>
        <div class="space-y-2">
          <h3 class="text-sm font-semibold text-fg">Pré-deploy</h3>
          <p class="text-xs text-fg-muted">Confirme os dados do agente antes de implantar.</p>
        </div>
        <dl class="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div>
            <dt class="text-[11px] uppercase tracking-wider text-fg-dim">Nome</dt>
            <dd class="mt-0.5 text-sm text-fg">${escapeHtml(agent?.name ?? '')}</dd>
          </div>
          <div>
            <dt class="text-[11px] uppercase tracking-wider text-fg-dim">ID</dt>
            <dd class="mt-0.5 font-mono text-xs text-fg">${escapeHtml(agentId)}</dd>
          </div>
        </dl>
      </efs-card>

      ${deployError ? errorMessage(deployError) : ''}

      <div class="flex items-center justify-end gap-2">
        ${button({
          label: deploying ? 'Implantando…' : 'Implantar agente',
          loading: deploying,
          attrs: { 'data-deploy': true },
        })}
      </div>
    </div>
  `;
}

function deployedViewHtml() {
  const enabledOk = enabledStatus?.enabled ?? true;
  const triggerUrl = publicBaseUrl
    ? `${publicBaseUrl}/api/aihub/workflows/${encodeURIComponent(/** @type {Workflow} */ (workflow).id)}/trigger`
    : `/api/aihub/workflows/${encodeURIComponent(/** @type {Workflow} */ (workflow).id)}/trigger`;

  return `
    <div class="space-y-5">
      <efs-card extraClasses="border-success/40 bg-success/5">
        <div class="flex items-start gap-3">
          <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-success/10 text-success">
            ${BoltIcon('h-5 w-5')}
          </div>
          <div class="min-w-0 flex-1">
            <h3 class="text-sm font-semibold text-fg">Implantado</h3>
            <p class="mt-1 text-xs text-fg-muted">
              Workflow ID: <code class="font-mono">${escapeHtml(/** @type {Workflow} */ (workflow).id)}</code>
            </p>
            ${enabledOk
              ? badge('Ativo', { tone: 'success', extraClasses: 'mt-2' })
              : `<p class="mt-2 rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
                   Workflow tem ${(enabledStatus?.disabledAgentIds ?? []).length} agente(s) desabilitado(s) — runtime vai pular essas chamadas.
                 </p>`}
          </div>
        </div>
      </efs-card>

      <efs-card>
        <div class="space-y-3">
          <div>
            <h3 class="text-sm font-semibold text-fg">Como consumir</h3>
            <p class="mt-1 text-xs text-fg-muted">Trigger HTTP do workflow. Identidade vai por headers <code>x-efs-account</code> + <code>x-efs-project-id</code>.</p>
          </div>
          <pre class="overflow-x-auto rounded-md bg-bg-soft px-3 py-2 font-mono text-[11px] text-fg-muted">curl -X POST '${escapeHtml(triggerUrl)}' \\
  -H 'Content-Type: application/json' \\
  -H 'x-efs-account: &lt;sua-conta&gt;' \\
  -H 'x-efs-project-id: &lt;projeto&gt;' \\
  -d '{"input": "..."}'</pre>
          <p class="text-[11px] text-fg-dim">A resposta retorna <code>executionId</code>; consulte <code>/api/aihub/executions/&#123;id&#125;/stream</code> (SSE) ou <code>/events</code> (polling fallback) pra acompanhar progresso.</p>
        </div>
      </efs-card>
    </div>
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
