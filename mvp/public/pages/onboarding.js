// @ts-check
/**
 * Página /onboarding (root vanilla). Substitui mvp/src/routes/Onboarding.tsx.
 *
 * Fluxo:
 *   1. User digita nome + conta
 *   2. Após 400ms (debounce), salva identidade provisória (sem projectId) e
 *      faz GET /projects pra listar projetos do tenant.
 *   3. User escolhe projeto, submete; identidade completa é salva e a página
 *      redireciona pra /dashboard.
 *
 * Se o user já chegou aqui com identity completa (name+account+projectId),
 * redireciona imediatamente pra /dashboard sem render.
 */

import { getIdentity, setIdentity } from '../lib/identity.js';
import { listProjects } from '../lib/projects.js';
import { friendlyError } from '../lib/api.js';
import { LogoIcon } from '../lib/icons.js';

const FETCH_DEBOUNCE_MS = 400;

// Guard: se já tem identidade completa, vai direto pra /dashboard.
const initial = getIdentity();
if (initial?.account && initial.projectId) {
  window.location.replace('/dashboard');
}

// Render do logo (SVG inline string).
const logoMount = document.getElementById('logo-mount');
if (logoMount) logoMount.innerHTML = LogoIcon('h-6 w-6');

const form = /** @type {HTMLFormElement | null} */ (document.getElementById('onboarding-form'));
const nameInput = /** @type {HTMLInputElement} */ (document.getElementById('field-name'));
const accountInput = /** @type {HTMLInputElement} */ (document.getElementById('field-account'));
const projectSelect = /** @type {HTMLSelectElement} */ (document.getElementById('field-project'));
const projectError = /** @type {HTMLElement} */ (document.getElementById('project-error'));
const formError = /** @type {HTMLElement} */ (document.getElementById('form-error'));
const submitBtn = /** @type {HTMLButtonElement} */ (document.getElementById('submit-btn'));

if (!form || !nameInput || !accountInput || !projectSelect || !projectError || !formError || !submitBtn) {
  throw new Error('Onboarding: elementos do formulário não encontrados');
}

// Pré-popula campos com identidade provisória existente.
if (initial?.name) nameInput.value = initial.name;
if (initial?.account) accountInput.value = initial.account;

/** @type {Array<{ id: string, name: string }>} */
let projects = [];
let loading = false;
/** @type {number | null} */
let debounceHandle = null;
/** @type {{ cancelled: boolean } | null} */
let activeFetch = null;

/** @param {string} placeholder */
function setProjectPlaceholder(placeholder) {
  projectSelect.innerHTML = `<option value="">${placeholder}</option>`;
  projectSelect.value = '';
  projectSelect.disabled = true;
}

function refreshProjectOptions() {
  if (!accountInput.value.trim()) {
    setProjectPlaceholder('Informe a conta primeiro');
    return;
  }
  if (loading) {
    setProjectPlaceholder('Carregando…');
    return;
  }
  if (projects.length === 0) {
    setProjectPlaceholder('Nenhum projeto disponível');
    return;
  }

  const previousValue = projectSelect.value;
  projectSelect.innerHTML = '<option value="">Selecione um projeto</option>' +
    projects.map((p) => `<option value="${p.id}">${escapeAttr(p.name)}</option>`).join('');
  projectSelect.disabled = false;

  if (previousValue && projects.some((p) => p.id === previousValue)) {
    projectSelect.value = previousValue;
  } else if (projects.length === 1) {
    projectSelect.value = projects[0].id;
  } else {
    projectSelect.value = '';
  }
}

/** @param {string} v */
function escapeAttr(v) {
  return v.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function refreshSubmitState() {
  const ok = nameInput.value.trim() && accountInput.value.trim() && projectSelect.value;
  submitBtn.disabled = !ok;
}

/** @param {string | null} message */
function setError(message) {
  if (message) {
    formError.textContent = message;
    formError.classList.remove('hidden');
  } else {
    formError.textContent = '';
    formError.classList.add('hidden');
  }
}

/** @param {string | null} message */
function setProjectFieldError(message) {
  if (message) {
    projectError.textContent = message;
    projectError.classList.remove('hidden');
  } else {
    projectError.textContent = '';
    projectError.classList.add('hidden');
  }
}

function scheduleFetch() {
  const trimmedAccount = accountInput.value.trim();
  if (!trimmedAccount) {
    projects = [];
    loading = false;
    setProjectFieldError(null);
    refreshProjectOptions();
    refreshSubmitState();
    return;
  }

  if (debounceHandle !== null) window.clearTimeout(debounceHandle);
  if (activeFetch) activeFetch.cancelled = true;

  loading = true;
  setProjectFieldError(null);
  refreshProjectOptions();
  refreshSubmitState();

  const fetchToken = { cancelled: false };
  activeFetch = fetchToken;

  debounceHandle = window.setTimeout(async () => {
    if (fetchToken.cancelled) return;

    // Identidade provisória sem projectId — pra que api.js envie x-efs-account.
    setIdentity({
      name: nameInput.value.trim(),
      account: trimmedAccount,
      projectId: '',
      projectName: '',
    });

    try {
      const list = await listProjects();
      if (fetchToken.cancelled) return;
      projects = list;
    } catch (err) {
      if (fetchToken.cancelled) return;
      projects = [];
      setProjectFieldError(friendlyError(err, 'Não foi possível carregar os projetos.'));
    } finally {
      if (!fetchToken.cancelled) {
        loading = false;
        refreshProjectOptions();
        refreshSubmitState();
      }
    }
  }, FETCH_DEBOUNCE_MS);
}

nameInput.addEventListener('input', () => {
  refreshSubmitState();
});

accountInput.addEventListener('input', () => {
  setError(null);
  scheduleFetch();
});

projectSelect.addEventListener('change', () => {
  refreshSubmitState();
});

form.addEventListener('submit', (e) => {
  e.preventDefault();
  const name = nameInput.value.trim();
  const account = accountInput.value.trim();
  const projectId = projectSelect.value;
  if (!name || !account || !projectId) return;

  const selected = projects.find((p) => p.id === projectId);
  setIdentity({
    name,
    account,
    projectId,
    projectName: selected?.name ?? '',
  });

  // Próximo passo da jornada — Dashboard ainda é React durante a migração,
  // mas absorve identity correta. Após Fase 3, /dashboard vira vanilla.
  window.location.assign('/dashboard');
});

// Render inicial caso o user retorne com account já preenchido.
if (accountInput.value.trim()) scheduleFetch();
refreshSubmitState();
