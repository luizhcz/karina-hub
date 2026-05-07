// @ts-check
/**
 * Página /agentes/:id/sandbox — chat streaming token-by-token via SSE
 * (POST com body, fetch + ReadableStream). Substitui
 * mvp/src/routes/AgentSandbox.tsx.
 *
 * Cleanup garantido em pagehide via AbortController — sem leaks de stream
 * quando user navega ou fecha aba.
 */

import { getAgent } from '../lib/agents.js';
import { createSession, streamRun } from '../lib/agent-sessions.js';
import { ApiError, friendlyError } from '../lib/api.js';
import {
  AgentIcon,
  ArrowLeftIcon,
  ArrowRightIcon,
  PlusIcon,
} from '../lib/icons.js';
import { button, errorMessage } from '../lib/ui.js';

/** @typedef {import('../lib/agents.js').Agent} Agent */
/** @typedef {import('../lib/agent-sessions.js').AgentSession} AgentSession */
/** @typedef {import('../lib/agent-sessions.js').StreamEvent} StreamEvent */

/** @typedef {{ kind: 'user', id: string, text: string }} UserMsg */
/** @typedef {{ id: string, name: string, result?: string, error?: string }} ToolCall */
/** @typedef {{ kind: 'assistant', id: string, content: string, toolCalls: ToolCall[], streaming: boolean, errored?: boolean }} AssistantMsg */
/** @typedef {UserMsg | AssistantMsg} ChatMsg */

const root = /** @type {HTMLElement} */ (document.getElementById('sandbox-page'));
const agentId = window.location.pathname.split('/')[2] ?? '';

/** @type {Agent | null} */
let agent = null;
let agentLoading = true;
/** @type {string | null} */
let agentError = null;

/** @type {AgentSession | null} */
let session = null;
/** @type {ChatMsg[]} */
let messages = [];
let inputText = '';
let sending = false;
/** @type {string | null} */
let turnError = null;

/** @type {AbortController | null} */
let abortCtl = null;

if (!agentId) {
  agentError = 'ID do agente ausente na URL.';
  agentLoading = false;
  renderShell();
} else {
  loadAgent();
}

window.addEventListener('pagehide', () => abortCtl?.abort(), { once: true });

async function loadAgent() {
  agentLoading = true;
  renderShell();
  try {
    agent = await getAgent(agentId);
  } catch (err) {
    agentError = friendlyError(err, 'Não foi possível carregar o agente.');
  } finally {
    agentLoading = false;
    renderShell();
  }
}

function renderShell() {
  if (agentLoading) {
    root.innerHTML = `
      <efs-card>
        <div class="flex items-center justify-center py-12">
          <efs-spinner class="inline-flex h-6 w-6 text-fg-muted"></efs-spinner>
        </div>
      </efs-card>
    `;
    return;
  }

  if (agentError || !agent) {
    root.innerHTML = errorMessage(agentError ?? 'Agente não encontrado.');
    return;
  }

  const modelLabel = agent.data?.model
    ? /** @type {any} */ (agent.data.model).predefinedModelId || /** @type {any} */ (agent.data.model).deploymentName
    : '';

  root.innerHTML = `
    <div class="mb-4 flex items-center justify-between gap-4">
      <div class="min-w-0">
        ${button({
          label: 'Voltar',
          variant: 'ghost',
          size: 'sm',
          leftIcon: ArrowLeftIcon('h-4 w-4'),
          extraClasses: '-ml-2 mb-1',
          attrs: { 'data-back': true },
        })}
        <div class="flex items-center gap-3">
          <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-success/10 text-success">
            ${AgentIcon('h-5 w-5')}
          </div>
          <div class="min-w-0">
            <h1 class="truncate text-2xl font-semibold tracking-tight text-fg">${escapeHtml(agent.name)}</h1>
            ${modelLabel ? `<p class="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-fg-dim">Sandbox · ${escapeHtml(modelLabel)}</p>` : ''}
          </div>
        </div>
      </div>
      ${button({
        label: 'Nova sessão',
        variant: 'secondary',
        size: 'sm',
        leftIcon: PlusIcon('h-4 w-4'),
        attrs: { 'data-new-session': true },
      })}
    </div>

    ${agent.enabled === false
      ? `<efs-card extraClasses="mb-4 border-warning/40 bg-warning/10">
           <p class="text-sm font-medium text-warning">
             Este agente está desabilitado em produção. O sandbox continua funcionando, mas
             invocar este agente em workflows reais resultará em pulo silencioso.
           </p>
         </efs-card>`
      : ''}

    <efs-card padded="false" extraClasses="flex flex-1 flex-col overflow-hidden">
      <div id="msg-scroller" class="flex-1 space-y-4 overflow-y-auto px-5 py-5"></div>
      <div id="turn-error" class="hidden border-t border-border px-5 py-2"></div>
      <div class="border-t border-border bg-bg-soft/50 px-4 py-3">
        <div class="flex items-end gap-2">
          <textarea id="input-area" rows="1" placeholder="Pergunte algo ao agente…"
                    class="block max-h-32 min-h-[40px] flex-1 resize-none rounded-lg border border-border bg-surface px-3 py-2 text-sm text-fg placeholder:text-fg-dim focus:outline-none focus:ring-2 focus:ring-accent/30 focus:border-accent disabled:cursor-not-allowed disabled:opacity-60">${escapeHtml(inputText)}</textarea>
          <button id="send-btn" type="button" aria-label="Enviar mensagem"
                  class="inline-flex h-9 items-center justify-center gap-2 rounded-lg bg-accent px-4 text-sm font-medium text-accent-contrast hover:bg-accent-soft disabled:cursor-not-allowed disabled:opacity-50 shrink-0">
            <span>Enviar</span>
            ${ArrowRightIcon('h-4 w-4')}
          </button>
        </div>
        <p class="mt-1.5 text-[11px] text-fg-dim">Enter pra enviar · Shift+Enter pra quebrar linha</p>
      </div>
    </efs-card>
  `;

  root.querySelector('[data-back]')?.addEventListener('click', () => window.location.assign('/agentes'));
  root.querySelector('[data-new-session]')?.addEventListener('click', handleNewSession);

  /** @type {HTMLTextAreaElement | null} */
  const ta = root.querySelector('#input-area');
  ta?.addEventListener('input', () => {
    inputText = ta.value;
    refreshSendBtn();
  });
  ta?.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  });

  root.querySelector('#send-btn')?.addEventListener('click', handleSend);

  renderMessages();
  refreshSendBtn();
}

function refreshSendBtn() {
  const btn = /** @type {HTMLButtonElement | null} */ (root.querySelector('#send-btn'));
  if (!btn) return;
  btn.disabled = !inputText.trim() || sending;
  if (sending) {
    btn.innerHTML = `
      <svg viewBox="0 0 24 24" fill="none" class="animate-spin h-4 w-4"><circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" class="opacity-20"/><path d="M22 12a10 10 0 0 1-10 10" stroke="currentColor" stroke-width="3" stroke-linecap="round"/></svg>
      <span>Enviando…</span>
    `;
  } else {
    btn.innerHTML = `<span>Enviar</span>${ArrowRightIcon('h-4 w-4')}`;
  }
  const ta = /** @type {HTMLTextAreaElement | null} */ (root.querySelector('#input-area'));
  if (ta) ta.disabled = sending;
}

function handleNewSession() {
  abortCtl?.abort();
  session = null;
  messages = [];
  inputText = '';
  turnError = null;
  renderShell();
}

function renderMessages() {
  const scroller = /** @type {HTMLElement | null} */ (root.querySelector('#msg-scroller'));
  if (!scroller) return;

  if (messages.length === 0) {
    scroller.innerHTML = `
      <div class="flex flex-col items-center justify-center px-6 py-14 text-center">
        <div class="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-accent-subtle text-accent">
          ${AgentIcon('h-6 w-6')}
        </div>
        <h3 class="text-base font-semibold text-fg">Nenhuma mensagem ainda</h3>
        <p class="mt-1 max-w-sm text-sm text-fg-muted">
          Envie uma pergunta abaixo pra conversar com o agente em modo sandbox. Ferramentas e
          modelos reais são executados, então cada turno conta no consumo do projeto.
        </p>
      </div>
    `;
    return;
  }

  scroller.innerHTML = messages.map((m) => m.kind === 'user' ? userBubbleHtml(m) : assistantBubbleHtml(m)).join('');
  scroller.scrollTo({ top: scroller.scrollHeight, behavior: 'smooth' });
}

function renderTurnError() {
  const errEl = /** @type {HTMLElement | null} */ (root.querySelector('#turn-error'));
  if (!errEl) return;
  if (turnError) {
    errEl.textContent = turnError;
    errEl.className = 'border-t border-border px-5 py-2 text-xs text-danger';
  } else {
    errEl.className = 'hidden';
    errEl.textContent = '';
  }
}

/** @param {UserMsg} m */
function userBubbleHtml(m) {
  return `
    <div class="flex justify-end">
      <div class="max-w-[75%] whitespace-pre-wrap break-words rounded-2xl rounded-br-sm bg-accent px-4 py-2.5 text-sm leading-relaxed text-accent-contrast shadow-soft">
        ${escapeHtml(m.text)}
      </div>
    </div>
  `;
}

/** @param {AssistantMsg} m */
function assistantBubbleHtml(m) {
  const showTyping = m.streaming && m.content.length === 0 && m.toolCalls.length === 0;
  const bubbleClasses = m.errored
    ? 'border-danger/30 bg-danger/10 text-fg'
    : 'border-border bg-surface text-fg';

  const typing = `
    <span class="flex h-5 items-center gap-1">
      <span class="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce" style="animation-delay:0ms"></span>
      <span class="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce" style="animation-delay:150ms"></span>
      <span class="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce" style="animation-delay:300ms"></span>
    </span>
  `;

  return `
    <div class="flex justify-start">
      <div class="flex max-w-[85%] flex-col items-start gap-2">
        ${m.toolCalls.map(toolCallChipHtml).join('')}
        ${(m.content.length > 0 || m.errored || showTyping)
          ? `<div class="rounded-2xl rounded-bl-sm border px-4 py-2.5 text-sm leading-relaxed shadow-card ${bubbleClasses}">
               ${showTyping ? typing : `<div class="whitespace-pre-wrap break-words">${escapeHtml(m.content)}</div>`}
             </div>`
          : ''}
      </div>
    </div>
  `;
}

/** @param {ToolCall} call */
function toolCallChipHtml(call) {
  const status = call.error ? 'error' : call.result !== undefined ? 'done' : 'running';
  const label = call.name || 'tool';
  const borderClass = status === 'error' ? 'border-danger/30' : 'border-border';
  const summaryColor = status === 'running' ? 'text-accent' : status === 'error' ? 'text-danger' : 'text-fg-muted';
  const icon = status === 'running'
    ? '<svg viewBox="0 0 24 24" fill="none" class="animate-spin h-3 w-3"><circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" class="opacity-20"/><path d="M22 12a10 10 0 0 1-10 10" stroke="currentColor" stroke-width="3" stroke-linecap="round"/></svg>'
    : `<span aria-hidden="true">${status === 'error' ? '⚠' : '✓'}</span>`;

  return `
    <details class="group max-w-full rounded-md border bg-bg-soft text-xs ${borderClass}">
      <summary class="flex cursor-pointer items-center gap-2 px-3 py-1.5 select-none ${summaryColor}">
        ${icon}
        <span class="font-medium">tool: ${escapeHtml(label)}</span>
        ${status === 'running' ? '<span class="text-fg-dim">executando…</span>' : ''}
      </summary>
      ${(call.result || call.error)
        ? `<pre class="max-h-40 overflow-auto border-t border-border bg-surface px-3 py-2 font-mono text-[11px] text-fg">${escapeHtml(call.error ?? call.result ?? '')}</pre>`
        : ''}
    </details>
  `;
}

async function handleSend() {
  const text = inputText.trim();
  if (!text || sending) return;

  inputText = '';
  turnError = null;
  sending = true;
  renderTurnError();

  /** @type {UserMsg} */
  const userMsg = { kind: 'user', id: `u-${shortId()}`, text };
  /** @type {AssistantMsg} */
  const assistantMsg = { kind: 'assistant', id: `a-${shortId()}`, content: '', toolCalls: [], streaming: true };
  messages = [...messages, userMsg, assistantMsg];

  // Atualiza UI sem reset do textarea (já está vazio).
  const ta = /** @type {HTMLTextAreaElement | null} */ (root.querySelector('#input-area'));
  if (ta) ta.value = '';
  refreshSendBtn();
  renderMessages();

  abortCtl = new AbortController();

  /** @param {(msg: AssistantMsg) => AssistantMsg} mut */
  const updateAssistant = (mut) => {
    messages = messages.map((m) => (m.id === assistantMsg.id && m.kind === 'assistant' ? mut(m) : m));
    renderMessages();
  };

  try {
    if (!session) session = await createSession(agentId);
    for await (const event of streamRun(agentId, session.sessionId, text, abortCtl.signal)) {
      applyEvent(event, updateAssistant);
      if (event.type === 'done' || event.type === 'error') break;
    }
    updateAssistant((m) => ({ ...m, streaming: false }));
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') {
      updateAssistant((m) => ({ ...m, streaming: false }));
      return;
    }
    const msg = err instanceof ApiError && err.status === 404
      ? 'Sessão expirou. Comece uma nova sessão pra continuar.'
      : friendlyError(err, 'Falha ao enviar mensagem.');
    turnError = msg;
    renderTurnError();
    updateAssistant((m) => ({ ...m, streaming: false, errored: true }));
  } finally {
    sending = false;
    abortCtl = null;
    refreshSendBtn();
  }
}

/**
 * @param {StreamEvent} event
 * @param {(mut: (msg: AssistantMsg) => AssistantMsg) => void} updateAssistant
 */
function applyEvent(event, updateAssistant) {
  switch (event.type) {
    case 'text-delta':
      updateAssistant((m) => ({ ...m, content: m.content + event.delta }));
      break;
    case 'tool-start':
      updateAssistant((m) => ({
        ...m,
        toolCalls: [...m.toolCalls, { id: event.toolCallId, name: event.toolName }],
      }));
      break;
    case 'tool-result':
      updateAssistant((m) => ({
        ...m,
        toolCalls: m.toolCalls.map((c) => (c.id === event.toolCallId ? { ...c, result: event.result } : c)),
      }));
      break;
    case 'tool-error':
      updateAssistant((m) => ({
        ...m,
        toolCalls: m.toolCalls.map((c) => (c.id === event.toolCallId ? { ...c, error: event.error } : c)),
      }));
      break;
    case 'error':
      updateAssistant((m) => ({
        ...m,
        content: m.content || event.message,
        errored: true,
        streaming: false,
      }));
      break;
    case 'done':
      break;
  }
}

function shortId() {
  return Math.random().toString(36).slice(2, 10);
}

/** @param {string} v */
function escapeHtml(v) {
  return String(v ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
