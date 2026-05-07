// @ts-check
/**
 * <efs-spinner> — substitui mvp/src/ui/Spinner.tsx.
 *
 * Atributos:
 *   - class — classes adicionais (controla tamanho, cor)
 *
 * Uso:
 *   <efs-spinner class="h-4 w-4 text-fg-muted"></efs-spinner>
 */

class EfsSpinner extends HTMLElement {
  connectedCallback() {
    this.innerHTML = `
      <svg viewBox="0 0 24 24" fill="none" class="animate-spin text-current h-full w-full">
        <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" class="opacity-20" />
        <path d="M22 12a10 10 0 0 1-10 10" stroke="currentColor" stroke-width="3" stroke-linecap="round" />
      </svg>
    `;
  }
}

customElements.define('efs-spinner', EfsSpinner);
