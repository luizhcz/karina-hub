// @ts-check
/**
 * <efs-card> — substitui mvp/src/ui/Card.tsx.
 *
 * Atributos:
 *   - padded (boolean, default true) — adiciona padding interno
 *   - interactive (boolean) — hover state pra indicar clicável
 *   - class — classes adicionais
 *
 * Uso:
 *   <efs-card>
 *     conteúdo
 *   </efs-card>
 *
 *   <efs-card interactive class="cursor-pointer">
 *     ...
 *   </efs-card>
 */

class EfsCard extends HTMLElement {
  connectedCallback() {
    const padded = this.getAttribute('padded') !== 'false';
    const interactive = this.hasAttribute('interactive');
    const extra = this.getAttribute('class') ?? '';

    const base = 'rounded-2xl border border-border bg-surface shadow-sm';
    const padding = padded ? 'p-5' : '';
    const hoverState = interactive
      ? 'transition-all duration-200 hover:-translate-y-0.5 hover:border-accent/40 hover:shadow-md'
      : '';

    // Não removemos extra do atributo — apenas concatenamos via className interno.
    // Isso preserva ARIA/data-* atributos no host. Wrapping com div interno
    // mantém estilo controlado e permite re-render sem lock-in.
    const inner = document.createElement('div');
    inner.className = [base, padding, hoverState].filter(Boolean).join(' ');

    // Preserva filhos.
    while (this.firstChild) inner.appendChild(this.firstChild);
    this.appendChild(inner);

    // Aplica extras direto no host (transparente).
    if (extra) this._extraClasses = extra;
  }
}

customElements.define('efs-card', EfsCard);

/**
 * <efs-card-header> — header dentro de um card.
 *
 * Atributos:
 *   - title (string)
 *   - description (string)
 *
 * Slot via filhos com [data-efs-actions]:
 *   <efs-card-header title="X" description="Y">
 *     <div data-efs-actions><button>...</button></div>
 *   </efs-card-header>
 */
class EfsCardHeader extends HTMLElement {
  connectedCallback() {
    const title = this.getAttribute('title') ?? '';
    const description = this.getAttribute('description') ?? '';

    /** @type {Node | null} */
    let actionsNode = null;
    for (const child of Array.from(this.children)) {
      if (child instanceof HTMLElement && child.hasAttribute('data-efs-actions')) {
        actionsNode = child;
        break;
      }
    }

    const wrapper = document.createElement('div');
    wrapper.className = 'flex items-start justify-between gap-3';
    wrapper.innerHTML = `
      <div class="min-w-0">
        <h3 class="text-sm font-semibold text-fg">${title}</h3>
        ${description ? `<p class="mt-1 text-xs text-fg-muted">${description}</p>` : ''}
      </div>
    `;

    if (actionsNode) {
      const actionsHost = document.createElement('div');
      actionsHost.className = 'flex shrink-0 items-center gap-2';
      // Move filhos do node original.
      while (actionsNode.firstChild) actionsHost.appendChild(actionsNode.firstChild);
      actionsNode.remove();
      wrapper.appendChild(actionsHost);
    }

    this.innerHTML = '';
    this.appendChild(wrapper);
  }
}

customElements.define('efs-card-header', EfsCardHeader);
