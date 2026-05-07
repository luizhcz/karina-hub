// @ts-check
/**
 * Página /ferramentas/nova e /ferramentas/:id — editor de generic tool.
 * Substitui mvp/src/routes/ToolEditor.tsx.
 *
 * SIMPLIFICAÇÃO Fase 2: o JsonSchemaBuilder visual (687 LOC, src/ui/JsonSchemaBuilder.tsx)
 * vira textarea pra schema JSON cru com validação básica. Versão visual
 * completa fica pra Fase 4 (PR 4c).
 */

import {
  createGenericTool,
  extractPlaceholders,
  getGenericTool,
  updateGenericTool,
} from '../lib/generic-tools.js';
import { friendlyError } from '../lib/api.js';
import { ArrowLeftIcon } from '../lib/icons.js';
import {
  badge,
  button,
  errorMessage,
  inputField,
  selectField,
  textareaField,
} from '../lib/ui.js';

/** @typedef {import('../lib/generic-tools.js').GenericTool} GenericTool */
/** @typedef {import('../lib/generic-tools.js').SaveGenericToolBody} SaveGenericToolBody */
/** @typedef {import('../lib/generic-tools.js').ParamDefinition} ParamDefinition */

const root = /** @type {HTMLElement} */ (document.getElementById('tool-editor-page'));

const segment = window.location.pathname.split('/')[2] ?? '';
/** @type {'create' | 'edit'} */
const mode = segment === 'nova' ? 'create' : 'edit';
const editId = mode === 'edit' ? segment : null;

/** @type {'params' | 'headers' | 'body' | 'response'} */
let activeTab = 'params';

const form = {
  name: '',
  description: '',
  whenToUse: '',
  /** @type {'GET' | 'POST'} */
  method: 'GET',
  url: '',
  /** @type {Record<string, ParamDefinition>} */
  pathParams: {},
  /** @type {Record<string, ParamDefinition>} */
  queryParams: {},
  /** @type {Record<string, string>} */
  customHeaders: {},
  /** @type {'None' | 'Json' | 'Text' | 'FormUrlEncoded'} */
  inputContentType: 'None',
  inputSchema: '',
  /** @type {'Json' | 'Text' | 'Csv'} */
  outputContentType: 'Json',
  outputSchema: '',
  timeoutSeconds: '',
};

let loading = mode === 'edit';
/** @type {string | null} */
let loadError = null;
/** @type {string | null} */
let formError = null;
let submitting = false;

if (mode === 'edit' && editId) {
  loadTool(editId);
} else {
  renderShell();
}

/** @param {string} id */
async function loadTool(id) {
  loading = true;
  renderShell();
  try {
    const tool = await getGenericTool(id);
    form.name = tool.name;
    form.description = tool.description;
    form.whenToUse = tool.whenToUse ?? '';
    form.method = tool.httpMethod;
    form.url = tool.urlTemplate;
    form.pathParams = { ...tool.pathParams };
    form.queryParams = { ...tool.queryParams };
    form.customHeaders = { ...tool.customHeaders };
    form.inputContentType = tool.inputContentType;
    form.inputSchema = tool.inputSchema ?? '';
    form.outputContentType = tool.outputContentType;
    form.outputSchema = tool.outputSchema ?? '';
    form.timeoutSeconds = tool.timeoutSecondsOverride !== null ? String(tool.timeoutSecondsOverride) : '';
  } catch (err) {
    loadError = friendlyError(err, 'Não foi possível carregar a ferramenta.');
  } finally {
    loading = false;
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

  if (loadError) {
    root.innerHTML = errorMessage(loadError);
    return;
  }

  const title = mode === 'edit' ? 'Editar ferramenta' : 'Nova ferramenta';
  const detectedPlaceholders = extractPlaceholders(form.url);

  root.innerHTML = `
    <div class="mb-6">
      ${button({ label: 'Voltar', variant: 'ghost', size: 'sm', leftIcon: ArrowLeftIcon('h-4 w-4'), extraClasses: '-ml-2 mb-1', attrs: { 'data-back': true } })}
      <h1 class="text-2xl font-semibold tracking-tight text-fg">${title}</h1>
      <p class="mt-1 text-sm text-fg-muted">Endpoint HTTP que seus agentes podem invocar. Aceita placeholders <code class="font-mono">{nome}</code> na URL pra path params.</p>
    </div>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Identificação</h3>
        </div>
        ${inputField({ label: 'Nome', name: 'name', value: form.name, placeholder: 'Ex.: get_client_position', autofocus: mode === 'create' })}
        ${textareaField({ label: 'Descrição', name: 'description', value: form.description, placeholder: 'O que esta ferramenta faz?', rows: 2 })}
        ${textareaField({ label: 'Quando usar (opcional)', name: 'whenToUse', value: form.whenToUse, placeholder: 'Hint pro LLM decidir invocar — ex.: "use quando user perguntar sobre saldo"', rows: 2 })}
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Endpoint</h3>
          <p class="mt-1 text-xs text-fg-muted">Use <code class="font-mono">{nome}</code> pra placeholders de path params (ex.: <code class="font-mono">/users/{userId}</code>).</p>
        </div>
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-12">
          <div class="sm:col-span-3">
            ${selectField({
              label: 'Método',
              name: 'method',
              value: form.method,
              options: [
                { value: 'GET', label: 'GET' },
                { value: 'POST', label: 'POST' },
              ],
            })}
          </div>
          <div class="sm:col-span-9">
            ${inputField({ label: 'URL Template', name: 'url', value: form.url, placeholder: 'https://api.example.com/users/{userId}', monospace: true })}
          </div>
        </div>
        ${detectedPlaceholders.length > 0
          ? `<div class="flex flex-wrap gap-1.5 text-[11px] text-fg-muted">
               <span>Placeholders detectados:</span>
               ${detectedPlaceholders.map((p) => badge(p, { tone: 'accent' })).join('')}
             </div>`
          : ''}
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div class="flex items-center gap-1 border-b border-border" id="tabs"></div>
        <div id="tab-content"></div>
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Timeout</h3>
        </div>
        <div class="max-w-xs">
          ${inputField({ label: 'Timeout (segundos, opcional)', name: 'timeoutSeconds', value: form.timeoutSeconds, type: 'number', placeholder: 'default 30' })}
        </div>
      </div>
    </efs-card>

    <div id="form-error" class="mb-4">${formError ? errorMessage(formError) : ''}</div>

    <div class="flex items-center justify-end gap-2">
      ${button({ label: 'Cancelar', variant: 'ghost', attrs: { 'data-cancel': true } })}
      ${button({
        label: mode === 'edit' ? 'Salvar alterações' : 'Cadastrar ferramenta',
        loading: submitting,
        attrs: { 'data-submit': true },
      })}
    </div>
  `;

  renderTabs();
  wireForm();
}

function renderTabs() {
  const tabsEl = /** @type {HTMLElement} */ (root.querySelector('#tabs'));
  if (!tabsEl) return;

  /** @type {Array<{ key: 'params' | 'headers' | 'body' | 'response', label: string }>} */
  const tabs = [
    { key: 'params', label: 'Parâmetros' },
    { key: 'headers', label: 'Headers' },
    { key: 'body', label: 'Body (input)' },
    { key: 'response', label: 'Response (output)' },
  ];

  tabsEl.innerHTML = tabs.map((t) => {
    const isActive = activeTab === t.key;
    const stateClasses = isActive ? 'text-fg' : 'text-fg-muted hover:text-fg';
    return `
      <button type="button" data-tab="${t.key}"
              class="relative px-4 py-2.5 text-sm font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30 ${stateClasses}">
        ${t.label}
        ${isActive ? '<span class="absolute inset-x-0 bottom-0 h-0.5 bg-accent" aria-hidden="true"></span>' : ''}
      </button>
    `;
  }).join('');

  tabsEl.querySelectorAll('[data-tab]').forEach((btn) => {
    btn.addEventListener('click', () => {
      activeTab = /** @type {any} */ (btn.getAttribute('data-tab'));
      renderTabs();
      renderTabContent();
    });
  });

  renderTabContent();
}

function renderTabContent() {
  const content = /** @type {HTMLElement} */ (root.querySelector('#tab-content'));
  if (!content) return;

  switch (activeTab) {
    case 'params':
      content.innerHTML = `
        <div class="space-y-5">
          <div>
            <h4 class="text-xs font-semibold uppercase tracking-wider text-fg-dim mb-2">Path params</h4>
            <p class="text-xs text-fg-muted mb-3">Detectados automaticamente dos <code class="font-mono">{placeholders}</code> da URL.</p>
            <efs-param-rows-editor id="path-params" empty-hint="Sem path params — use {nome} na URL." locked-hint="vem da URL"></efs-param-rows-editor>
          </div>
          <div>
            <h4 class="text-xs font-semibold uppercase tracking-wider text-fg-dim mb-2">Query params</h4>
            <efs-param-rows-editor id="query-params" empty-hint="Sem query params."></efs-param-rows-editor>
          </div>
        </div>
      `;
      setTimeout(() => {
        /** @type {any} */
        const pathEd = root.querySelector('#path-params');
        if (pathEd) {
          pathEd.lockedKeys = extractPlaceholders(form.url);
          pathEd.value = form.pathParams;
          pathEd.addEventListener('change', (/** @type {CustomEvent} */ e) => {
            form.pathParams = e.detail;
          });
        }
        /** @type {any} */
        const queryEd = root.querySelector('#query-params');
        if (queryEd) {
          queryEd.value = form.queryParams;
          queryEd.addEventListener('change', (/** @type {CustomEvent} */ e) => {
            form.queryParams = e.detail;
          });
        }
      }, 0);
      break;

    case 'headers':
      content.innerHTML = `
        <div>
          <p class="text-xs text-fg-muted mb-3">Headers customizados enviados a cada chamada (Authorization, X-API-Key, etc).</p>
          <efs-kv-editor id="headers-editor" key-placeholder="header" val-label="valor" val-placeholder="ex.: Bearer \${TOKEN}"></efs-kv-editor>
        </div>
      `;
      setTimeout(() => {
        /** @type {any} */
        const ed = root.querySelector('#headers-editor');
        if (ed) {
          ed.value = form.customHeaders;
          ed.addEventListener('change', (/** @type {CustomEvent} */ e) => {
            form.customHeaders = e.detail;
          });
        }
      }, 0);
      break;

    case 'body':
      content.innerHTML = `
        <div class="space-y-4">
          <div class="max-w-xs">
            ${selectField({
              label: 'Content-Type',
              name: 'inputContentType',
              value: form.inputContentType,
              options: [
                { value: 'None', label: 'Nenhum' },
                { value: 'Json', label: 'application/json' },
                { value: 'Text', label: 'text/plain' },
                { value: 'FormUrlEncoded', label: 'application/x-www-form-urlencoded' },
              ],
            })}
          </div>
          ${form.inputContentType !== 'None' ? `
            <div>
              <label class="block text-xs font-medium text-fg-muted mb-1.5">JSON Schema do body</label>
              <textarea name="inputSchema" rows="8" placeholder='{"type":"object","properties":{...}}'
                        class="w-full rounded-lg border border-border bg-surface px-3 py-2 font-mono text-[12px] text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30">${escapeHtml(form.inputSchema)}</textarea>
              <p class="mt-1 text-[11px] text-fg-dim">JSON cru. (Editor visual completo virá na Fase 4.)</p>
            </div>
          ` : ''}
        </div>
      `;
      const inputCtSel = /** @type {HTMLSelectElement | null} */ (content.querySelector('[name="inputContentType"]'));
      inputCtSel?.addEventListener('change', () => {
        form.inputContentType = /** @type {any} */ (inputCtSel.value);
        renderTabContent();
      });
      const inputSchemaArea = /** @type {HTMLTextAreaElement | null} */ (content.querySelector('[name="inputSchema"]'));
      inputSchemaArea?.addEventListener('input', () => { form.inputSchema = inputSchemaArea.value; });
      break;

    case 'response':
      content.innerHTML = `
        <div class="space-y-4">
          <div class="max-w-xs">
            ${selectField({
              label: 'Content-Type',
              name: 'outputContentType',
              value: form.outputContentType,
              options: [
                { value: 'Json', label: 'application/json' },
                { value: 'Text', label: 'text/plain' },
                { value: 'Csv', label: 'text/csv' },
              ],
            })}
          </div>
          <div>
            <label class="block text-xs font-medium text-fg-muted mb-1.5">JSON Schema da resposta (opcional)</label>
            <textarea name="outputSchema" rows="6" placeholder='{"type":"object",...}'
                      class="w-full rounded-lg border border-border bg-surface px-3 py-2 font-mono text-[12px] text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30">${escapeHtml(form.outputSchema)}</textarea>
            <p class="mt-1 text-[11px] text-fg-dim">JSON cru. (Editor visual completo virá na Fase 4.)</p>
          </div>
        </div>
      `;
      const outputCtSel = /** @type {HTMLSelectElement | null} */ (content.querySelector('[name="outputContentType"]'));
      outputCtSel?.addEventListener('change', () => {
        form.outputContentType = /** @type {any} */ (outputCtSel.value);
      });
      const outputSchemaArea = /** @type {HTMLTextAreaElement | null} */ (content.querySelector('[name="outputSchema"]'));
      outputSchemaArea?.addEventListener('input', () => { form.outputSchema = outputSchemaArea.value; });
      break;
  }
}

function wireForm() {
  root.querySelector('[data-back]')?.addEventListener('click', () => window.location.assign('/ferramentas'));
  root.querySelector('[data-cancel]')?.addEventListener('click', () => window.location.assign('/ferramentas'));
  root.querySelector('[data-submit]')?.addEventListener('click', onSave);

  /** @param {string} name @param {(v: string) => void} setter */
  const wireField = (name, setter) => {
    const el = /** @type {HTMLInputElement | HTMLTextAreaElement | null} */ (root.querySelector(`[name="${name}"]`));
    el?.addEventListener('input', () => setter(el.value));
  };
  wireField('name', (v) => { form.name = v; });
  wireField('description', (v) => { form.description = v; });
  wireField('whenToUse', (v) => { form.whenToUse = v; });
  wireField('timeoutSeconds', (v) => { form.timeoutSeconds = v; });

  const methodSel = /** @type {HTMLSelectElement | null} */ (root.querySelector('[name="method"]'));
  methodSel?.addEventListener('change', () => {
    form.method = /** @type {any} */ (methodSel.value);
  });

  const urlInput = /** @type {HTMLInputElement | null} */ (root.querySelector('[name="url"]'));
  urlInput?.addEventListener('input', () => {
    form.url = urlInput.value;
    // Atualiza locked keys do path-params editor + badges de placeholders
    /** @type {any} */
    const pathEd = root.querySelector('#path-params');
    if (pathEd) pathEd.lockedKeys = extractPlaceholders(form.url);
    // Re-render do bloco de detectados
    renderShell();
  });
}

async function onSave() {
  formError = null;
  const name = form.name.trim();
  const description = form.description.trim();
  const url = form.url.trim();

  if (!name) return setFormError('Informe um nome.');
  if (!description) return setFormError('Informe uma descrição.');
  if (!url) return setFormError('Informe a URL.');
  if (!/^https?:\/\//i.test(url)) return setFormError('URL precisa começar com http:// ou https://.');

  // Validação de schemas — se preenchido, deve ser JSON parseável.
  if (form.inputContentType !== 'None' && form.inputSchema.trim()) {
    try { JSON.parse(form.inputSchema); }
    catch { return setFormError('JSON Schema do body inválido.'); }
  }
  if (form.outputSchema.trim()) {
    try { JSON.parse(form.outputSchema); }
    catch { return setFormError('JSON Schema da resposta inválido.'); }
  }

  submitting = true;
  renderShell();

  /** @type {SaveGenericToolBody} */
  const body = {
    id: editId ?? generateId(),
    name,
    description,
    httpMethod: form.method,
    urlTemplate: url,
    pathParams: form.pathParams,
    queryParams: form.queryParams,
    customHeaders: form.customHeaders,
    inputContentType: form.inputContentType,
    inputSchema: form.inputSchema.trim() || null,
    outputContentType: form.outputContentType,
    outputSchema: form.outputSchema.trim() || null,
    timeoutSecondsOverride: form.timeoutSeconds.trim() ? Number(form.timeoutSeconds.trim()) : null,
    whenToUse: form.whenToUse.trim() || null,
  };

  try {
    if (mode === 'edit' && editId) {
      await updateGenericTool(editId, body);
    } else {
      await createGenericTool(body);
    }
    window.location.assign('/ferramentas');
  } catch (err) {
    formError = friendlyError(err, 'Não foi possível salvar.');
    submitting = false;
    renderShell();
  }
}

/** @param {string} message */
function setFormError(message) {
  formError = message;
  const el = root.querySelector('#form-error');
  if (el) el.innerHTML = errorMessage(message);
}

function generateId() {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  const seg = () => Math.random().toString(36).slice(2, 10);
  return `${seg()}-${seg()}-${seg()}-${seg()}`;
}

/** @param {string} v */
function escapeHtml(v) {
  return String(v ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
