// @ts-check
/**
 * Página /dashboard — analytics do projeto. 2 fases de fetch + SVG sparkline.
 * Substitui mvp/src/routes/Dashboard.tsx.
 */

import {
  getProjectAgents,
  getProjectBudget,
  getProjectOverview,
  getProjectTimeseries,
} from '../lib/project-analytics.js';
import { listAgents } from '../lib/agents.js';
import { ApiError, friendlyError } from '../lib/api.js';
import { getIdentity } from '../lib/identity.js';
import { isInCurrentProject } from '../lib/project-scope.js';
import { badge, button, errorMessage } from '../lib/ui.js';

/** @typedef {import('../lib/project-analytics.js').ProjectOverview} ProjectOverview */
/** @typedef {import('../lib/project-analytics.js').ProjectTimeseriesBucket} ProjectTimeseriesBucket */
/** @typedef {import('../lib/project-analytics.js').ProjectAgentBreakdown} ProjectAgentBreakdown */
/** @typedef {import('../lib/project-analytics.js').ProjectBudgetStatus} ProjectBudgetStatus */

const root = /** @type {HTMLElement} */ (document.getElementById('dashboard-page'));
const projectId = getIdentity()?.projectId ?? null;

/** @type {'day' | 'hour'} */
let granularity = 'day';
/** @type {'line' | 'bar'} */
let chartType = 'line';

/** @type {{ overview: ProjectOverview, timeseries: ProjectTimeseriesBucket[], agents: ProjectAgentBreakdown[], budget: ProjectBudgetStatus, hiddenAgents: ProjectAgentBreakdown[] } | null} */
let data = null;
let loading = true;
/** @type {string | null} */
let pageError = null;
let forbidden = false;

if (!projectId) {
  root.innerHTML = `
    <efs-card>
      <p class="text-center text-sm text-fg-muted">Selecione um projeto pra abrir o dashboard de uso.</p>
    </efs-card>
  `;
} else {
  reload();
}

async function reload() {
  if (!projectId) return;
  loading = true;
  pageError = null;
  forbidden = false;
  renderShell();

  try {
    // Fase 1: descobrir agent IDs próprios vs hidden (Visibility=global cross-project).
    const [agents, allAgents] = await Promise.all([
      getProjectAgents(projectId, 20),
      listAgents(),
    ]);
    const ownAgentIds = new Set(allAgents.filter(isInCurrentProject).map((a) => a.id));
    const visibleAgents = agents.filter((a) => ownAgentIds.has(a.agentId));
    const hiddenAgents = agents.filter((a) => !ownAgentIds.has(a.agentId));
    const hiddenIds = hiddenAgents.map((a) => a.agentId);

    // Fase 2: overview/timeseries/budget com excludeAgentIds.
    const [overview, timeseries, budget] = await Promise.all([
      getProjectOverview(projectId),
      getProjectTimeseries(projectId, granularity, hiddenIds),
      getProjectBudget(projectId),
    ]);

    // Desconta hidden dos totais.
    const sub = hiddenAgents.reduce(
      (acc, a) => {
        acc.cost += a.costUsd;
        acc.tokens += a.totalTokens;
        acc.calls += a.calls;
        const fail = Math.round(a.calls * a.errorRate);
        acc.failed += fail;
        acc.completed += Math.max(0, a.calls - fail);
        return acc;
      },
      { cost: 0, tokens: 0, calls: 0, completed: 0, failed: 0 },
    );
    const adjCompleted = Math.max(0, overview.completed - sub.completed);
    const adjFailed = Math.max(0, overview.failed - sub.failed);
    const totalResolved = adjCompleted + adjFailed;
    /** @type {ProjectOverview} */
    const adjustedOverview = {
      ...overview,
      totalCostUsd: Math.max(0, overview.totalCostUsd - sub.cost),
      totalTokens: Math.max(0, overview.totalTokens - sub.tokens),
      totalCalls: Math.max(0, overview.totalCalls - sub.calls),
      totalExecutions: Math.max(0, overview.totalExecutions - sub.calls),
      completed: adjCompleted,
      failed: adjFailed,
      successRate: totalResolved > 0 ? adjCompleted / totalResolved : 0,
      topAgents: overview.topAgents.filter((a) => ownAgentIds.has(a.agentId)),
    };

    data = {
      overview: adjustedOverview,
      timeseries,
      agents: visibleAgents,
      budget,
      hiddenAgents,
    };
  } catch (err) {
    if (err instanceof ApiError && err.status === 403) {
      forbidden = true;
      data = null;
    } else {
      pageError = friendlyError(err, 'Não foi possível carregar o dashboard.');
    }
  } finally {
    loading = false;
    renderShell();
  }
}

function renderShell() {
  let bodyHtml = '';
  if (loading && !data) {
    bodyHtml = `
      <efs-card>
        <div class="flex items-center justify-center py-12">
          <efs-spinner class="inline-flex h-6 w-6 text-fg-muted"></efs-spinner>
        </div>
      </efs-card>
    `;
  } else if (forbidden) {
    bodyHtml = errorMessage('Você não tem permissão pra ver analytics de outro projeto. Confirme com o time de governança se precisa de acesso adicional.');
  } else if (pageError) {
    bodyHtml = errorMessage(pageError);
  } else if (data) {
    bodyHtml = renderData(data);
  }

  root.innerHTML = `
    <div class="mb-8 flex items-end justify-between gap-4">
      <div>
        <h1 class="text-[28px] font-semibold tracking-tight text-fg">Dashboard</h1>
        <p class="mt-2 text-sm text-fg-muted">
          Uso e custo do seu projeto no mês corrente. Dados defasados em até 30 minutos (refresh do agregador de custos).
        </p>
      </div>
      ${button({ label: 'Atualizar', variant: 'secondary', size: 'sm', attrs: { 'data-reload': true } })}
    </div>
    <div id="dashboard-body" class="space-y-6">${bodyHtml}</div>
  `;

  root.querySelector('[data-reload]')?.addEventListener('click', () => reload());
  wireToggles();
}

/** @param {NonNullable<typeof data>} d */
function renderData(d) {
  const successPct = d.overview.completed + d.overview.failed > 0 ? d.overview.successRate : null;

  return `
    ${overviewCardsHtml(d.overview, successPct)}

    <efs-card>
      <div class="space-y-4">
        <div class="flex items-end justify-between gap-3">
          <div>
            <h3 class="text-sm font-semibold text-fg">Custo e execuções no período</h3>
            <p class="mt-1 text-xs text-fg-muted">${chartType === 'line' ? 'Cada ponto representa o intervalo escolhido (dia ou hora).' : 'Cada barra representa o intervalo escolhido (dia ou hora).'}</p>
          </div>
          <div class="flex shrink-0 items-center gap-2">
            ${chartTypeToggleHtml()}
            ${granularityToggleHtml()}
          </div>
        </div>
        ${sparkHtml(d.timeseries)}
        ${d.hiddenAgents.length > 0 ? `
          <p class="text-[11px] leading-relaxed text-fg-dim">
            Custo, tokens e LLM calls já descontam os ${d.hiddenAgents.length} ${d.hiddenAgents.length === 1 ? 'agente interno' : 'agentes internos'} da plataforma.
            A contagem de execuções/falhas é por workflow (granularidade diferente) — pode incluir
            runs do assistente de perfil/gerador de test cases.
          </p>` : ''}
      </div>
    </efs-card>

    <efs-card>
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Agentes do projeto</h3>
          <p class="mt-1 text-xs text-fg-muted">Top 20 por custo no período. p95 reflete a duração das chamadas LLM no agente; error rate é por execução do workflow envolvendo o agente.</p>
        </div>
        ${agentsTableHtml(d.agents)}
        ${d.hiddenAgents.length > 0 ? `
          <p class="text-[11px] leading-relaxed text-fg-dim">
            ${d.hiddenAgents.length} ${d.hiddenAgents.length === 1 ? 'agente interno oculto' : 'agentes internos ocultos'} (assistente de perfil, gerador de test cases).
            Consumo de <strong>${formatUsd(d.hiddenAgents.reduce((s, a) => s + a.costUsd, 0))}</strong> já foi descontado dos cards e da taxa de sucesso acima.
          </p>` : ''}
      </div>
    </efs-card>

    ${budgetCardHtml(d.budget)}
  `;
}

/**
 * @param {ProjectOverview} overview
 * @param {number | null} successPct
 */
function overviewCardsHtml(overview, successPct) {
  const successValue = successPct === null ? '—' : `${(successPct * 100).toFixed(1)}%`;
  const successSecondary = successPct === null
    ? 'sem execuções no período'
    : successPct >= 0.95 ? 'dentro do esperado' : 'abaixo do esperado';

  const topAgentsHtml = overview.topAgents.length > 0 ? `
    <efs-card extraClasses="sm:col-span-2 lg:col-span-4">
      <p class="text-[11px] font-semibold uppercase tracking-wider text-fg-dim">Top agentes por custo</p>
      <ul class="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-3">
        ${overview.topAgents.map((a, idx) => `
          <li class="flex items-center justify-between rounded-md border border-border bg-bg-soft px-3 py-2 text-xs">
            <div class="flex min-w-0 items-center gap-2">
              <span class="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent-subtle text-[10px] font-bold text-accent">${idx + 1}</span>
              <span class="truncate font-mono">${escapeHtml(a.agentId)}</span>
            </div>
            <span class="ml-2 shrink-0 font-mono font-semibold">${formatUsd(a.costUsd)}</span>
          </li>
        `).join('')}
      </ul>
    </efs-card>
  ` : '';

  return `
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      ${kpiCardHtml('Custo MTD', formatUsd(overview.totalCostUsd), `${formatNumber(overview.totalCalls)} ${overview.totalCalls === 1 ? 'chamada' : 'chamadas'} de LLM`)}
      ${kpiCardHtml('Tokens MTD', formatNumber(overview.totalTokens), 'entrada + saída')}
      ${kpiCardHtml('Execuções', formatNumber(overview.totalExecutions), `${overview.completed} ok · ${overview.failed} ${overview.failed === 1 ? 'falha' : 'falhas'}`)}
      ${kpiCardHtml('Taxa de sucesso', successValue, successSecondary)}
      ${topAgentsHtml}
    </div>
  `;
}

/** @param {string} label @param {string} value @param {string} [secondary] */
function kpiCardHtml(label, value, secondary) {
  return `
    <efs-card>
      <p class="text-[11px] font-semibold uppercase tracking-wider text-fg-dim">${escapeHtml(label)}</p>
      <p class="mt-1 text-2xl font-semibold tracking-tight text-fg">${escapeHtml(value)}</p>
      ${secondary ? `<p class="mt-1 text-xs text-fg-muted">${escapeHtml(secondary)}</p>` : ''}
    </efs-card>
  `;
}

function granularityToggleHtml() {
  /** @param {'day' | 'hour'} g @param {string} label */
  const tab = (g, label) => {
    const active = granularity === g;
    const cls = active ? 'bg-accent text-accent-contrast' : 'text-fg-muted hover:text-fg';
    return `<button type="button" data-granularity="${g}" class="rounded-md px-3 py-1.5 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30 ${cls}" aria-pressed="${active}">${label}</button>`;
  };
  return `<div class="inline-flex shrink-0 rounded-lg border border-border bg-surface p-0.5">${tab('day', 'Dia')}${tab('hour', 'Hora')}</div>`;
}

function chartTypeToggleHtml() {
  /** @param {'line' | 'bar'} t @param {string} label */
  const tab = (t, label) => {
    const active = chartType === t;
    const cls = active ? 'bg-accent text-accent-contrast' : 'text-fg-muted hover:text-fg';
    return `<button type="button" data-chart-type="${t}" class="rounded-md px-3 py-1.5 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30 ${cls}" aria-pressed="${active}">${label}</button>`;
  };
  return `<div class="inline-flex shrink-0 rounded-lg border border-border bg-surface p-0.5">${tab('line', 'Linha')}${tab('bar', 'Barras')}</div>`;
}

function wireToggles() {
  root.querySelectorAll('[data-granularity]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const next = /** @type {'day' | 'hour'} */ (btn.getAttribute('data-granularity'));
      if (next === granularity) return;
      granularity = next;
      reload();
    });
  });
  root.querySelectorAll('[data-chart-type]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const next = /** @type {'line' | 'bar'} */ (btn.getAttribute('data-chart-type'));
      if (next === chartType) return;
      chartType = next;
      // Só re-render do bloco de gráfico — não precisa refetch.
      renderShell();
    });
  });
}

/** @param {ProjectTimeseriesBucket[]} buckets */
function sparkHtml(buckets) {
  if (buckets.length === 0) {
    return '<p class="rounded-md bg-bg-soft px-3 py-4 text-center text-xs text-fg-muted">Sem execuções no período.</p>';
  }

  const maxCost = Math.max(...buckets.map((b) => b.costUsd), 0.0001);
  const maxExecs = Math.max(...buckets.map((b) => b.executions), 1);
  const maxFailed = Math.max(...buckets.map((b) => b.failed), 0);

  const chart = chartType === 'bar'
    ? sparkBarsHtml(buckets, maxCost)
    : sparkLinesHtml(buckets, maxCost, maxFailed);

  return `
    <div class="space-y-3">
      ${chart}
      <div class="flex justify-between text-[10px] text-fg-dim">
        <span>${escapeHtml(formatBucket(buckets[0].bucket))}</span>
        ${buckets.length > 2 ? `<span>${escapeHtml(formatBucket(buckets[Math.floor(buckets.length / 2)].bucket))}</span>` : ''}
        <span>${escapeHtml(formatBucket(buckets[buckets.length - 1].bucket))}</span>
      </div>
      <div class="flex flex-wrap items-center gap-4 text-[11px] text-fg-muted">
        <span class="flex items-center gap-1.5"><span class="h-2 w-3 rounded-sm bg-accent" aria-hidden="true"></span>custo (máx ${formatUsd(maxCost)})</span>
        ${(chartType === 'line' || maxFailed > 0) ? `<span class="flex items-center gap-1.5"><span class="h-2 w-3 rounded-sm bg-warning" aria-hidden="true"></span>falhas (máx ${maxExecs} execs)</span>` : ''}
      </div>
    </div>
  `;
}

/** @param {ProjectTimeseriesBucket[]} buckets @param {number} maxCost */
function sparkBarsHtml(buckets, maxCost) {
  return `
    <div class="flex h-40 items-end gap-1">
      ${buckets.map((b) => {
        const costPct = (b.costUsd / maxCost) * 100;
        const errPct = b.executions > 0 ? (b.failed / b.executions) * 100 : 0;
        const tooltip = `${formatBucket(b.bucket)} — ${formatUsd(b.costUsd)} / ${formatNumber(b.executions)} execuções (${b.failed} ${b.failed === 1 ? 'falha' : 'falhas'})`;
        return `
          <div class="group relative flex h-full flex-1 items-end">
            <div class="w-full overflow-hidden rounded-t-sm bg-accent transition-opacity group-hover:opacity-80"
                 style="height: ${Math.max(costPct, 2)}%"
                 title="${escapeAttr(tooltip)}">
              ${errPct > 0 ? `<div class="bg-warning" style="height: ${errPct}%" aria-hidden="true"></div>` : ''}
            </div>
          </div>
        `;
      }).join('')}
    </div>
  `;
}

/** @param {ProjectTimeseriesBucket[]} buckets @param {number} maxCost @param {number} maxFailed */
function sparkLinesHtml(buckets, maxCost, maxFailed) {
  const W = 100, H = 100;
  const n = buckets.length;
  const xAt = (/** @type {number} */ i) => (n === 1 ? W / 2 : (i / (n - 1)) * W);
  const costPoints = buckets.map((b, i) => `${xAt(i).toFixed(2)},${(H - (b.costUsd / maxCost) * H).toFixed(2)}`).join(' ');
  const failPoints = maxFailed > 0
    ? buckets.map((b, i) => `${xAt(i).toFixed(2)},${(H - (b.failed / maxFailed) * H).toFixed(2)}`).join(' ')
    : '';
  const costArea = `0,${H} ${costPoints} ${W},${H}`;

  const dotsHtml = buckets.map((b, i) => {
    const left = (xAt(i) / W) * 100;
    const top = (1 - b.costUsd / maxCost) * 100;
    const tooltip = `${formatBucket(b.bucket)} — ${formatUsd(b.costUsd)} / ${formatNumber(b.executions)} execuções (${b.failed} ${b.failed === 1 ? 'falha' : 'falhas'})`;
    return `<div class="absolute h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full bg-accent ring-2 ring-surface opacity-0 transition-opacity hover:opacity-100" style="left: ${left}%; top: ${top}%" title="${escapeAttr(tooltip)}"></div>`;
  }).join('');

  return `
    <div class="relative h-40 w-full">
      <svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" class="absolute inset-0 h-full w-full" aria-hidden="true">
        ${[25, 50, 75].map((y) => `<line x1="0" x2="${W}" y1="${y}" y2="${y}" stroke="currentColor" stroke-width="0.2" class="text-border" />`).join('')}
        <polyline points="${costArea}" fill="currentColor" class="text-accent/15" stroke="none" />
        <polyline points="${costPoints}" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round" class="text-accent" vector-effect="non-scaling-stroke" />
        ${failPoints ? `<polyline points="${failPoints}" fill="none" stroke="currentColor" stroke-width="1" stroke-dasharray="2 2" class="text-warning" vector-effect="non-scaling-stroke" />` : ''}
      </svg>
      ${dotsHtml}
    </div>
  `;
}

/** @param {ProjectAgentBreakdown[]} rows */
function agentsTableHtml(rows) {
  if (rows.length === 0) {
    return '<p class="rounded-md bg-bg-soft px-3 py-4 text-center text-xs text-fg-muted">Sem dados de uso por agente no período.</p>';
  }
  const head = `
    <thead>
      <tr class="border-b border-border text-[10px] uppercase tracking-wider text-fg-dim">
        <th class="py-2 pr-3 text-left font-semibold">Agente</th>
        <th class="py-2 px-3 text-left font-semibold">Modelo</th>
        <th class="py-2 px-3 text-right font-semibold">Calls</th>
        <th class="py-2 px-3 text-right font-semibold">Tokens</th>
        <th class="py-2 px-3 text-right font-semibold">Custo</th>
        <th class="py-2 px-3 text-right font-semibold">Avg</th>
        <th class="py-2 px-3 text-right font-semibold">p95</th>
        <th class="py-2 pl-3 text-right font-semibold">Erro</th>
      </tr>
    </thead>
  `;
  const body = rows.map((r) => `
    <tr class="border-b border-border last:border-b-0 hover:bg-bg-soft">
      <td class="py-2 pr-3 font-mono text-fg">${escapeHtml(r.agentId)}</td>
      <td class="py-2 px-3 text-fg-muted">${escapeHtml(r.modelId ?? '—')}</td>
      <td class="py-2 px-3 text-right font-mono">${formatNumber(r.calls)}</td>
      <td class="py-2 px-3 text-right font-mono">${formatNumber(r.totalTokens)}</td>
      <td class="py-2 px-3 text-right font-mono font-semibold">${formatUsd(r.costUsd)}</td>
      <td class="py-2 px-3 text-right font-mono">${Math.round(r.avgDurationMs)} ms</td>
      <td class="py-2 px-3 text-right font-mono">${Math.round(r.p95DurationMs)} ms</td>
      <td class="py-2 pl-3 text-right font-mono ${r.errorRate > 0.1 ? 'text-warning' : 'text-fg-muted'}">${(r.errorRate * 100).toFixed(1)}%</td>
    </tr>
  `).join('');

  return `<div class="overflow-x-auto"><table class="w-full text-xs">${head}<tbody>${body}</tbody></table></div>`;
}

/** @param {ProjectBudgetStatus} status */
function budgetCardHtml(status) {
  const noBudget = status.maxCostUsdPerDay == null && status.maxTokensPerDay == null;

  const description = noBudget
    ? 'Nenhum limite diário configurado neste projeto.'
    : 'Consumo de hoje vs limite configurado em ProjectSettings. Atenção: hoje a plataforma só registra alerta — execução não é bloqueada quando o limite é cruzado.';

  const bars = noBudget ? '' : `
    <div class="space-y-3">
      ${budgetBarHtml('Custo USD', status.todayCostUsd, status.maxCostUsdPerDay ?? null, status.costUsagePct ?? null, formatUsd)}
      ${budgetBarHtml('Tokens', status.todayTokens, status.maxTokensPerDay ?? null, status.tokensUsagePct ?? null, formatNumber)}
    </div>
  `;

  const status_message = status.exceeded
    ? '<div class="rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">Limite diário cruzado. As execuções continuam acontecendo — esse é um sinal pra revisar o uso e ajustar limite ou desligar agentes específicos manualmente.</div>'
    : (!noBudget ? badge('Dentro do orçamento', { tone: 'success' }) : '');

  return `
    <efs-card>
      <div class="space-y-3">
        <div>
          <h3 class="text-sm font-semibold text-fg">Orçamento diário</h3>
          <p class="mt-1 text-xs text-fg-muted">${escapeHtml(description)}</p>
        </div>
        ${bars}
        ${status_message}
      </div>
    </efs-card>
  `;
}

/**
 * @param {string} label
 * @param {number} current
 * @param {number | null} max
 * @param {number | null} pct
 * @param {(n: number) => string} format
 */
function budgetBarHtml(label, current, max, pct, format) {
  if (max == null || max === 0) {
    return `
      <div>
        <div class="flex items-baseline justify-between text-xs">
          <span class="font-semibold text-fg">${escapeHtml(label)}</span>
          <span class="font-mono text-fg-muted">${escapeHtml(format(current))} hoje · sem limite</span>
        </div>
      </div>
    `;
  }
  const safePct = Math.min(Math.max(pct ?? 0, 0), 1);
  const overshoot = (pct ?? 0) > 1;
  const fillColor = overshoot ? 'bg-warning' : safePct > 0.8 ? 'bg-warning' : 'bg-accent';
  return `
    <div>
      <div class="mb-1 flex items-baseline justify-between text-xs">
        <span class="font-semibold text-fg">${escapeHtml(label)}</span>
        <span class="font-mono text-fg-muted">${escapeHtml(format(current))} / ${escapeHtml(format(max))} (${((pct ?? 0) * 100).toFixed(0)}%)</span>
      </div>
      <div class="h-2 overflow-hidden rounded-full bg-bg-soft">
        <div class="h-full transition-all ${fillColor}" style="width: ${Math.max(safePct * 100, overshoot ? 100 : 0)}%" aria-hidden="true"></div>
      </div>
    </div>
  `;
}

// Helpers ────────────────────────────────────────────────────────────────────

/** @param {number} n */
function formatUsd(n) {
  if (n === 0) return '$0.00';
  if (n < 0.01) return `$${n.toFixed(4)}`;
  if (n < 1) return `$${n.toFixed(3)}`;
  return `$${n.toFixed(2)}`;
}

/** @param {number} n */
function formatNumber(n) {
  if (n < 1000) return String(n);
  return n.toLocaleString('pt-BR');
}

/** @param {string} iso */
function formatBucket(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' });
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
