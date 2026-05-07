// @ts-check
/**
 * <efs-empty-state> — substitui mvp/src/ui/EmptyState.tsx.
 *
 * Atributos:
 *   - title (string)
 *   - description (string, opcional)
 *
 * Slots via filhos:
 *   - [data-efs-icon] — ícone no topo (geralmente SVG)
 *   - [data-efs-action] — CTA no rodapé (botão)
 *
 * Uso:
 *   <efs-empty-state title="Nada por aqui" description="Crie seu primeiro item.">
 *     <div data-efs-icon>${ToolIcon('h-6 w-6')}</div>
 *     <div data-efs-action><button>Criar</button></div>
 *   </efs-empty-state>
 */

class EfsEmptyState extends HTMLElement {
  connectedCallback() {
    const title = this.getAttribute('title') ?? '';
    const description = this.getAttribute('description') ?? '';

    /** @type {HTMLElement | null} */
    let iconNode = null;
    /** @type {HTMLElement | null} */
    let actionNode = null;

    for (const child of Array.from(this.children)) {
      if (!(child instanceof HTMLElement)) continue;
      if (child.hasAttribute('data-efs-icon')) iconNode = child;
      else if (child.hasAttribute('data-efs-action')) actionNode = child;
    }

    const wrapper = document.createElement('div');
    wrapper.className = 'flex flex-col items-center px-6 py-14 text-center';
    wrapper.innerHTML = `
      ${
        iconNode
          ? '<div data-efs-icon-host class="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-accent-subtle text-accent"></div>'
          : ''
      }
      <h3 class="text-base font-semibold text-fg">${title}</h3>
      ${description ? `<p class="mt-1 max-w-sm text-sm text-fg-muted">${description}</p>` : ''}
      ${actionNode ? '<div data-efs-action-host class="mt-6"></div>' : ''}
    `;

    if (iconNode) {
      const iconHost = wrapper.querySelector('[data-efs-icon-host]');
      while (iconNode.firstChild) iconHost?.appendChild(iconNode.firstChild);
      iconNode.remove();
    }
    if (actionNode) {
      const actionHost = wrapper.querySelector('[data-efs-action-host]');
      while (actionNode.firstChild) actionHost?.appendChild(actionNode.firstChild);
      actionNode.remove();
    }

    this.innerHTML = '';
    this.appendChild(wrapper);
  }
}

customElements.define('efs-empty-state', EfsEmptyState);
