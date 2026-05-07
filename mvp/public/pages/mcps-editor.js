// @ts-check
/**
 * Página /mcps/nova e /mcps/:id — editor de MCP server. Substitui
 * mvp/src/routes/McpServerEditor.tsx.
 *
 * Mode detectado pelo path: '/mcps/nova' = create, '/mcps/<id>' = edit.
 */

import { createMcpServer, getMcpServer, updateMcpServer } from '../lib/mcp-servers.js';
import { friendlyError } from '../lib/api.js';
import { ArrowLeftIcon } from '../lib/icons.js';
import { button, errorMessage, inputField, selectField, textareaField } from '../lib/ui.js';

/** @typedef {import('../lib/mcp-servers.js').McpServer} McpServer */
/** @typedef {import('../lib/mcp-servers.js').SaveMcpServerBody} SaveMcpServerBody */

const root = /** @type {HTMLElement} */ (document.getElementById('mcp-editor-page'));

const segment = window.location.pathname.split('/')[2] ?? '';
/** @type {'create' | 'edit'} */
const mode = segment === 'nova' ? 'create' : 'edit';
const editId = mode === 'edit' ? segment : null;

const form = {
  name: '',
  description: '',
  serverLabel: '',
  serverUrl: '',
  /** @type {string[]} */
  allowedTools: [],
  /** @type {Record<string, string>} */
  headers: {},
  /** @type {'never' | 'always'} */
  requireApproval: 'never',
};

let loading = mode === 'edit';
/** @type {string | null} */
let loadError = null;
/** @type {string | null} */
let formError = null;
let submitting = false;

if (mode === 'edit' && editId) {
  loadServer(editId);
} else {
  renderShell();
}

/** @param {string} id */
async function loadServer(id) {
  loading = true;
  renderShell();
  try {
    const server = await getMcpServer(id);
    form.name = server.name;
    form.description = server.description ?? '';
    form.serverLabel = server.serverLabel;
    form.serverUrl = server.serverUrl;
    form.allowedTools = [...server.allowedTools];
    form.headers = { ...server.headers };
    form.requireApproval = server.requireApproval;
  } catch (err) {
    loadError = friendlyError(err, 'Não foi possível carregar o MCP.');
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

  const title = mode === 'edit' ? 'Editar MCP' : 'Novo MCP';

  root.innerHTML = `
    <div class="mb-6">
      ${button({ label: 'Voltar', variant: 'ghost', size: 'sm', leftIcon: ArrowLeftIcon('h-4 w-4'), extraClasses: '-ml-2 mb-1', attrs: { 'data-back': true } })}
      <h1 class="text-2xl font-semibold tracking-tight text-fg">${title}</h1>
      <p class="mt-1 text-sm text-fg-muted">Os agentes referenciam o MCP pelo identificador interno e o runtime resolve URL, label e tools permitidas a cada execução.</p>
    </div>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Identificação</h3>
          <p class="mt-1 text-xs text-fg-muted">Visível pra você e pros agentes deste projeto.</p>
        </div>
        ${inputField({ label: 'Nome', name: 'name', value: form.name, placeholder: 'Ex.: Filesystem Local', autofocus: mode === 'create' })}
        ${textareaField({ label: 'Descrição', name: 'description', value: form.description, placeholder: 'Quando o agente deve usar este MCP?' })}
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-4">
        <div>
          <h3 class="text-sm font-semibold text-fg">Conexão</h3>
          <p class="mt-1 text-xs text-fg-muted">Endereço público do servidor MCP. ServerLabel é o que o provider LLM enxerga.</p>
        </div>
        <div class="grid grid-cols-1 gap-4 md:grid-cols-3">
          <div>${inputField({ label: 'ServerLabel', name: 'serverLabel', value: form.serverLabel, placeholder: 'ex.: filesystem', monospace: true, hint: 'curto, sem espaços' })}</div>
          <div class="md:col-span-2">${inputField({ label: 'ServerUrl', name: 'serverUrl', value: form.serverUrl, placeholder: 'https://mcp.example.com/sse', monospace: true })}</div>
        </div>
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-3">
        <div>
          <h3 class="text-sm font-semibold text-fg">Tools permitidas</h3>
          <p class="mt-1 text-xs text-fg-muted">Whitelist de ferramentas do MCP que os agentes podem invocar. Pelo menos uma.</p>
        </div>
        <efs-string-list-editor id="tools-editor" placeholder="ex.: read_file" empty-hint="Nenhuma tool liberada ainda." monospace></efs-string-list-editor>
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-3">
        <div>
          <h3 class="text-sm font-semibold text-fg">Headers</h3>
          <p class="mt-1 text-xs text-fg-muted">Enviados em toda chamada ao MCP. Coloque tokens de autenticação aqui (ex.: Authorization).</p>
        </div>
        <efs-kv-editor id="headers-editor" key-placeholder="header" val-label="valor" val-placeholder="ex.: Bearer \${TOKEN}"></efs-kv-editor>
      </div>
    </efs-card>

    <efs-card extraClasses="mb-5">
      <div class="space-y-3">
        <div>
          <h3 class="text-sm font-semibold text-fg">Aprovação humana</h3>
          <p class="mt-1 text-xs text-fg-muted">Quando exigido, o agente pausa e espera HITL antes de invocar tools deste MCP.</p>
        </div>
        <div class="max-w-xs">
          ${selectField({
            name: 'requireApproval',
            value: form.requireApproval,
            options: [
              { value: 'never', label: 'Nunca pedir aprovação' },
              { value: 'always', label: 'Sempre pedir aprovação' },
            ],
          })}
        </div>
      </div>
    </efs-card>

    <div id="form-error" class="mb-4">${formError ? errorMessage(formError) : ''}</div>

    <div class="flex items-center justify-end gap-2">
      ${button({ label: 'Cancelar', variant: 'ghost', attrs: { 'data-cancel': true } })}
      ${button({
        label: mode === 'edit' ? 'Salvar alterações' : 'Cadastrar MCP',
        loading: submitting,
        attrs: { 'data-submit': true },
      })}
    </div>
  `;

  // Wire editores e listeners
  root.querySelector('[data-back]')?.addEventListener('click', () => window.location.assign('/mcps'));
  root.querySelector('[data-cancel]')?.addEventListener('click', () => window.location.assign('/mcps'));
  root.querySelector('[data-submit]')?.addEventListener('click', onSave);

  // Form fields
  /** @param {string} name @param {(v: string) => void} setter */
  const wireInput = (name, setter) => {
    const el = /** @type {HTMLInputElement | null} */ (root.querySelector(`[name="${name}"]`));
    el?.addEventListener('input', () => setter(el.value));
  };
  wireInput('name', (v) => { form.name = v; });
  wireInput('serverLabel', (v) => { form.serverLabel = v; });
  wireInput('serverUrl', (v) => { form.serverUrl = v; });

  const descArea = /** @type {HTMLTextAreaElement | null} */ (root.querySelector('[name="description"]'));
  descArea?.addEventListener('input', () => { form.description = descArea.value; });

  const approvalSel = /** @type {HTMLSelectElement | null} */ (root.querySelector('[name="requireApproval"]'));
  approvalSel?.addEventListener('change', () => {
    form.requireApproval = /** @type {'never' | 'always'} */ (approvalSel.value);
  });

  // Custom elements (depois do innerHTML pra que upgrade aconteça)
  setTimeout(() => {
    /** @type {any} */
    const toolsEditor = root.querySelector('#tools-editor');
    if (toolsEditor) {
      toolsEditor.value = form.allowedTools;
      toolsEditor.addEventListener('change', (/** @type {CustomEvent} */ e) => {
        form.allowedTools = e.detail;
      });
    }
    /** @type {any} */
    const headersEditor = root.querySelector('#headers-editor');
    if (headersEditor) {
      headersEditor.value = form.headers;
      headersEditor.addEventListener('change', (/** @type {CustomEvent} */ e) => {
        form.headers = e.detail;
      });
    }
  }, 0);
}

async function onSave() {
  formError = null;
  const name = form.name.trim();
  const serverLabel = form.serverLabel.trim();
  const serverUrl = form.serverUrl.trim();

  if (!name) return setFormError('Informe um nome para o MCP.');
  if (!serverLabel) return setFormError('Informe o ServerLabel (ex.: filesystem, github).');
  if (!serverUrl) return setFormError('Informe a URL do servidor MCP.');
  if (!/^https?:\/\//i.test(serverUrl)) return setFormError('URL precisa começar com http:// ou https://.');
  if (form.allowedTools.length === 0) return setFormError('Adicione ao menos uma tool permitida.');

  submitting = true;
  renderShell();

  /** @type {SaveMcpServerBody} */
  const body = {
    id: editId ?? generateId(),
    name,
    description: form.description.trim() || null,
    serverLabel,
    serverUrl,
    allowedTools: form.allowedTools,
    headers: form.headers,
    requireApproval: form.requireApproval,
  };

  try {
    if (mode === 'edit' && editId) {
      await updateMcpServer(editId, body);
    } else {
      await createMcpServer(body);
    }
    window.location.assign('/mcps');
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
