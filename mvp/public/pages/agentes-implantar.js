// @ts-check
/**
 * Página /agentes/:id/implantar — orquestração de deploy + auto-deploy de
 * avaliação com streaming SSE de progresso. Substitui
 * mvp/src/routes/AgentDeploy.tsx + AgentDeploy/{PresetSelector,EvalStatusCard,RerunEvalCard}.tsx.
 *
 * Simplificações Fase 3 (vs React):
 * - Versions modal + redeploy explícito: postergado pra Fase 4 fix (botão
 *   "Atualizar agente" virá após a Fase 4, junto da AgentEditor).
 * - RerunEvalCard: omitido (rerun via re-deploy quando AgentVersion muda).
 *
 * EvalStatusCard com SSE via streamEvalRun (lib/profile-evaluation.js) +
 * fallback polling de 5s quando SSE cai.
 */

import { getAgent } from '../lib/agents.js';
import {
  createWorkflow,
  deploymentWorkflowId,
  getWorkflow,
  getWorkflowEnabledStatus,
} from '../lib/workflows.js';
import {
  getEvalRun,
  listEvalRunsByAgent,
  presetMeta,
  runAutoDeploy,
  streamEvalRun,
} from '../lib/profile-evaluation.js';
import { getSystemInfo } from '../lib/system.js';
import { ApiError, friendlyError } from '../lib/api.js';
import { ArrowLeftIcon, BoltIcon, CheckIcon } from '../lib/icons.js';
import { badge, button, errorMessage } from '../lib/ui.js';

/** @typedef {import('../lib/agents.js').Agent} Agent */
/** @typedef {import('../lib/workflows.js').Workflow} Workflow */
/** @typedef {import('../lib/workflows.js').WorkflowEnabledStatus} WorkflowEnabledStatus */
/** @typedef {import('../lib/profile-evaluation.js').AutoDeployPreset} AutoDeployPreset */
/** @typedef {import('../lib/profile-evaluation.js').AutoDeployResponse} AutoDeployResponse */
/** @typedef {import('../lib/profile-evaluation.js').EvalProgressEvent} EvalProgressEvent */

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

/** @type {AutoDeployPreset} */
let preset = 'basic';
let confirmAdvancedOpen = false;
/** @type {AutoDeployResponse | null} */
let autoDeploy = null;
let autoDeployRunning = false;

/** @type {EvalProgressEvent | null} */
let evalProgress = null;
/** @type {(() => void) | null} */
let sseCloser = null;
/** @type {number | null} */
let pollHandle = null;

if (!agentId) {
  root.innerHTML = errorMessage('ID do agente ausente na URL.');
} else {
  loadAll();
}

window.addEventListener('pagehide', () => {
  sseCloser?.();
  if (pollHandle !== null) window.clearInterval(pollHandle);
}, { once: true });

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

    // Hidrata último auto-deploy (após F5).
    try {
      const runs = await listEvalRunsByAgent(agentId, 1);
      if (runs.length > 0) {
        const last = runs[0];
        const lastPreset = /** @type {AutoDeployPreset} */ (last.triggerContext?.preset ?? 'basic');
        preset = lastPreset;
        autoDeploy = {
          runId: last.runId,
          testSetVersionId: last.testSetVersionId,
          evaluatorConfigVersionId: last.evaluatorConfigVersionId,
          preset: lastPreset,
          caseCount: last.casesTotal,
          estimatedCostUsd: 0,
          estimatedDurationSeconds: 0,
          status: last.status,
          deduplicatedFromExisting: false,
          generatorFailed: false,
        };
        startEvalStream(last.runId);
      }
    } catch { /* sem runs ainda é caso comum */ }
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
  if (preset === 'advanced' && !confirmAdvancedOpen) {
    confirmAdvancedOpen = true;
    renderShell();
    return;
  }
  confirmAdvancedOpen = false;
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
    triggerEvalAutoDeploy(wf.id).catch(() => { /* ignore — soft gate */ });
  } catch (err) {
    deployError = friendlyError(err, 'Não foi possível concluir a implantação.');
  } finally {
    deploying = false;
    renderShell();
  }
}

/** @param {string} workflowId */
async function triggerEvalAutoDeploy(workflowId) {
  autoDeployRunning = true;
  renderShell();
  try {
    const result = await runAutoDeploy(agentId, preset, workflowId);
    autoDeploy = result;
    if (result.runId) startEvalStream(result.runId);
  } catch (err) {
    autoDeploy = {
      runId: null,
      testSetVersionId: null,
      evaluatorConfigVersionId: null,
      preset,
      caseCount: 0,
      estimatedCostUsd: 0,
      estimatedDurationSeconds: 0,
      status: null,
      deduplicatedFromExisting: false,
      generatorFailed: true,
    };
    console.warn('[AgentDeploy] auto-deploy falhou:', friendlyError(err));
  } finally {
    autoDeployRunning = false;
    renderShell();
  }
}

/** @param {string} runId */
function startEvalStream(runId) {
  sseCloser?.();
  if (pollHandle !== null) {
    window.clearInterval(pollHandle);
    pollHandle = null;
  }

  // Snapshot inicial via GET (cobre F5 quando run já existe).
  getEvalRun(runId).then((run) => {
    evalProgress = {
      status: run.status,
      casesTotal: run.casesTotal,
      casesCompleted: run.casesCompleted ?? 0,
      casesPassed: run.casesPassed ?? 0,
      casesFailed: run.casesFailed ?? 0,
      avgScore: run.avgScore ?? null,
      totalCostUsd: 0,
      totalTokens: 0,
      lastError: run.lastError ?? null,
      startedAt: run.startedAt ?? null,
      completedAt: run.completedAt ?? null,
    };
    renderShell();
  }).catch(() => { /* SSE cobre */ });

  // Stream SSE
  sseCloser = streamEvalRun(
    runId,
    (e) => { evalProgress = e; renderShell(); },
    (e) => { evalProgress = e; renderShell(); },
    () => {
      // SSE caiu, falhar pra polling
      sseCloser = null;
      startPolling(runId);
    },
  );
}

/** @param {string} runId */
function startPolling(runId) {
  if (pollHandle !== null) return;
  pollHandle = window.setInterval(async () => {
    try {
      const run = await getEvalRun(runId);
      const ev = {
        status: run.status,
        casesTotal: run.casesTotal,
        casesCompleted: run.casesCompleted ?? 0,
        casesPassed: run.casesPassed ?? 0,
        casesFailed: run.casesFailed ?? 0,
        avgScore: run.avgScore ?? null,
        totalCostUsd: 0,
        totalTokens: 0,
        lastError: run.lastError ?? null,
        startedAt: run.startedAt ?? null,
        completedAt: run.completedAt ?? null,
      };
      evalProgress = ev;
      renderShell();
      if (isTerminal(ev.status) && pollHandle !== null) {
        window.clearInterval(pollHandle);
        pollHandle = null;
      }
    } catch { /* retry silently */ }
  }, 5000);
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
        Cria um workflow Graph que expõe o agente para consumo via API. Após implantar, dispara automaticamente uma avaliação no preset escolhido.
      </p>
    </div>

    ${workflow ? deployedViewHtml() : pendingViewHtml()}
  `;

  root.querySelector('[data-back]')?.addEventListener('click', () => window.location.assign('/agentes'));
  wireActions();
  renderConfirmAdvanced();
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

      <efs-card>
        <div class="space-y-3">
          <div>
            <h3 class="text-sm font-semibold text-fg">Avaliação automática</h3>
            <p class="mt-1 text-xs text-fg-muted">Após o deploy, uma run é disparada com test cases sintéticos no preset escolhido.</p>
          </div>
          ${presetSelectorHtml()}
        </div>
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

      ${evalStatusCardHtml()}

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

function presetSelectorHtml() {
  const opts = /** @type {AutoDeployPreset[]} */ (['basic', 'medium', 'advanced']);
  return `
    <div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
      ${opts.map((p) => {
        const meta = presetMeta(p);
        const active = preset === p;
        const stateClasses = active
          ? 'border-accent bg-accent-subtle ring-2 ring-accent/30'
          : 'border-border bg-surface hover:border-accent/50';
        return `
          <button type="button" data-preset="${p}"
                  class="relative flex flex-col gap-2 rounded-lg border p-3 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40 ${stateClasses}">
            <span class="text-sm font-semibold text-fg">${escapeHtml(meta.label)}</span>
            <span class="text-[11px] text-fg-muted">${escapeHtml(meta.cost)} · ${escapeHtml(meta.duration)}</span>
            ${active ? `<span class="absolute right-2 top-2 text-accent">${CheckIcon('h-4 w-4')}</span>` : ''}
          </button>
        `;
      }).join('')}
    </div>
  `;
}

function evalStatusCardHtml() {
  if (!autoDeploy && !autoDeployRunning) return '';

  if (autoDeploy?.generatorFailed) {
    return `
      <efs-card>
        <div class="space-y-2">
          <h3 class="text-sm font-semibold text-fg">Avaliação não disponível</h3>
          <p class="text-xs text-fg-muted">O gerador de test cases não conseguiu produzir uma suíte agora. O deploy do workflow continua válido.</p>
          ${button({ label: 'Tentar novamente', variant: 'secondary', size: 'sm', attrs: { 'data-rerun-eval': true } })}
        </div>
      </efs-card>
    `;
  }

  if (!autoDeploy?.runId) {
    return `
      <efs-card>
        <div class="flex items-center gap-2 text-xs text-fg-muted">
          <efs-spinner class="inline-flex h-3 w-3"></efs-spinner>
          <span>Iniciando avaliação…</span>
        </div>
      </efs-card>
    `;
  }

  const meta = presetMeta(/** @type {AutoDeployPreset} */ (autoDeploy.preset));
  const status = evalProgress?.status ?? autoDeploy.status ?? 'Pending';
  const completed = evalProgress?.casesCompleted ?? 0;
  const total = evalProgress?.casesTotal ?? autoDeploy.caseCount;
  const passed = evalProgress?.casesPassed ?? 0;
  const failed = evalProgress?.casesFailed ?? 0;
  const score = evalProgress?.avgScore ?? null;
  const cost = evalProgress?.totalCostUsd ?? null;
  const pct = total > 0 ? Math.round((completed / total) * 100) : 0;
  const lowScore = isTerminal(status) && score !== null && Number(score) < 0.6;
  const allSkipped = isTerminal(status) && total > 0 && passed + failed === 0;
  const barColor = isTerminal(status)
    ? (lowScore ? 'bg-warning' : 'bg-success')
    : 'bg-accent';

  return `
    <efs-card>
      <div class="space-y-3">
        <div class="flex items-start justify-between gap-3">
          <div>
            <h3 class="text-sm font-semibold text-fg">Status da avaliação</h3>
            <p class="mt-1 text-xs text-fg-muted">Preset ${escapeHtml(meta.label)} · ${total} cases · custo estimado ${escapeHtml(meta.cost)}</p>
          </div>
          ${statusBadgeHtml(status)}
        </div>

        <div class="space-y-1">
          <div class="flex items-center justify-between text-[11px] text-fg-muted">
            <span>${completed}/${total} cases</span>
            <span>${pct}%</span>
          </div>
          <div class="h-2 w-full overflow-hidden rounded-full bg-bg-soft">
            <div class="h-full transition-all duration-500 ${barColor}" style="width: ${pct}%"></div>
          </div>
        </div>

        <div class="grid grid-cols-3 gap-3 text-center">
          ${statHtml('Passed', String(passed), 'text-success')}
          ${statHtml('Failed', String(failed), failed > 0 ? 'text-danger' : 'text-fg')}
          ${statHtml('Score', score !== null ? formatScore(score) : '—', lowScore ? 'text-warning' : 'text-fg')}
        </div>

        ${allSkipped ? `
          <div class="rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
            <p class="font-semibold">Preset ${escapeHtml(meta.label)} não é aplicável a este agente.</p>
            <p class="mt-1 text-fg-muted">Os evaluators Local só funcionam com agentes que tenham tools ou output literal previsível. Em agentes de chat livre, prefira <strong>Média</strong>.</p>
          </div>` : ''}

        ${lowScore && !allSkipped ? `
          <p class="rounded-md bg-warning/10 px-3 py-2 text-xs text-warning">
            Score abaixo do esperado — revise os resultados antes de habilitar consumo externo.
          </p>` : ''}

        ${!isTerminal(status) ? `
          <div class="flex items-center gap-2 text-[11px] text-fg-dim">
            <efs-spinner class="inline-flex h-3 w-3"></efs-spinner>
            <span>Avaliando em tempo real…</span>
          </div>` : ''}

        ${cost !== null && Number(cost) > 0 ? `<p class="text-[11px] text-fg-dim">Custo real: $${Number(cost).toFixed(4)} USD</p>` : ''}
      </div>
    </efs-card>
  `;
}

/** @param {string} label @param {string} value @param {string} toneClass */
function statHtml(label, value, toneClass) {
  return `
    <div class="rounded-lg border border-border bg-bg-soft px-3 py-2">
      <div class="text-base font-semibold ${toneClass}">${escapeHtml(value)}</div>
      <div class="text-[10px] uppercase tracking-wider text-fg-dim">${escapeHtml(label)}</div>
    </div>
  `;
}

/** @param {string} status */
function statusBadgeHtml(status) {
  if (status === 'Completed') return badge('Concluído', { tone: 'success' });
  if (status === 'Running')   return badge('Rodando', { tone: 'accent' });
  if (status === 'Pending')   return badge('Aguardando', { tone: 'neutral' });
  if (status === 'Failed')    return badge('Falhou', { tone: 'danger' });
  if (status === 'Cancelled') return badge('Cancelado', { tone: 'neutral' });
  return badge(status, { tone: 'neutral' });
}

function wireActions() {
  // Preset selector
  root.querySelectorAll('[data-preset]').forEach((btn) => {
    btn.addEventListener('click', () => {
      preset = /** @type {AutoDeployPreset} */ (btn.getAttribute('data-preset'));
      renderShell();
    });
  });

  // Deploy button
  root.querySelector('[data-deploy]')?.addEventListener('click', handleDeploy);

  // Rerun eval button (no caso de generatorFailed)
  root.querySelector('[data-rerun-eval]')?.addEventListener('click', () => {
    if (workflow) triggerEvalAutoDeploy(workflow.id);
  });
}

let confirmModalEl = /** @type {HTMLElement | null} */ (null);

function renderConfirmAdvanced() {
  if (confirmAdvancedOpen) {
    if (!confirmModalEl) {
      confirmModalEl = document.createElement('efs-modal');
      confirmModalEl.setAttribute('size', 'sm');
      confirmModalEl.setAttribute('title', 'Confirmar preset Avançada');
      confirmModalEl.setAttribute('description', 'Avaliação Avançada custa ~$0.50 USD e demora ~3min.');
      confirmModalEl.addEventListener('close', () => {
        confirmAdvancedOpen = false;
        confirmModalEl?.removeAttribute('open');
        renderShell();
      });
      document.body.appendChild(confirmModalEl);
    }
    confirmModalEl.innerHTML = `
      <p class="text-sm text-fg-muted">Use a Avançada quando precisar de cobertura completa (15 cases × 8 métricas MEAI). Pra ciclos rápidos de iteração, prefira Básica ou Média.</p>
      <div data-efs-footer class="flex justify-end gap-2">
        ${button({ label: 'Cancelar', variant: 'ghost', attrs: { 'data-confirm-cancel': true } })}
        ${button({ label: 'Confirmar e implantar', attrs: { 'data-confirm-ok': true } })}
      </div>
    `;
    confirmModalEl.setAttribute('open', '');
    confirmModalEl.querySelector('[data-confirm-cancel]')?.addEventListener('click', () => {
      confirmAdvancedOpen = false;
      confirmModalEl?.removeAttribute('open');
      renderShell();
    });
    confirmModalEl.querySelector('[data-confirm-ok]')?.addEventListener('click', () => {
      confirmAdvancedOpen = false;
      confirmModalEl?.removeAttribute('open');
      handleDeploy();
    });
  } else if (confirmModalEl) {
    confirmModalEl.removeAttribute('open');
  }
}

/** @param {string} status */
function isTerminal(status) {
  return status === 'Completed' || status === 'Failed' || status === 'Cancelled';
}

/** @param {number | string} score */
function formatScore(score) {
  const n = typeof score === 'string' ? parseFloat(score) : score;
  if (Number.isNaN(n)) return '—';
  return Math.round(n * 100).toString();
}

/** @param {string} v */
function escapeHtml(v) {
  return String(v ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
