// @ts-check
/**
 * Helpers DOM pra evitar boilerplate. Substitui composição JSX com:
 * - h(tag, attrs, ...children) → cria elemento
 * - html`<tag>...</tag>` → tagged template que retorna HTMLElement (parseado)
 * - mount(root, child) → substitui conteúdo do root pelo child
 * - escape(str) → escape HTML pra interpolação segura em html`...`
 */

/**
 * Escape HTML pra interpolação segura em strings de markup.
 * @param {unknown} value
 * @returns {string}
 */
export function escape(value) {
  if (value === null || value === undefined) return '';
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/**
 * @param {string} tag
 * @param {Record<string, unknown>} [attrs]
 * @param {...(Node | string | null | undefined | false)} children
 * @returns {HTMLElement}
 */
export function h(tag, attrs, ...children) {
  const el = document.createElement(tag);
  if (attrs) {
    for (const [key, value] of Object.entries(attrs)) {
      if (value === null || value === undefined || value === false) continue;
      if (key === 'class' || key === 'className') {
        el.setAttribute('class', String(value));
      } else if (key === 'style' && typeof value === 'object') {
        Object.assign(el.style, value);
      } else if (key === 'dataset' && typeof value === 'object') {
        for (const [k, v] of Object.entries(/** @type {object} */ (value))) {
          el.dataset[k] = String(v);
        }
      } else if (key.startsWith('on') && typeof value === 'function') {
        const event = key.slice(2).toLowerCase();
        el.addEventListener(event, /** @type {EventListener} */ (value));
      } else if (value === true) {
        el.setAttribute(key, '');
      } else {
        el.setAttribute(key, String(value));
      }
    }
  }
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    el.append(child instanceof Node ? child : String(child));
  }
  return el;
}

/**
 * Tagged template literal que retorna um DocumentFragment com os nodes
 * parseados. Interpolações são escapadas automaticamente. Pra interpolar
 * HTML pré-validado use html.raw().
 *
 * Uso: const node = html`<div class="x">${user.name}</div>`;
 *      root.appendChild(node);
 *
 * @param {TemplateStringsArray} strings
 * @param {...unknown} values
 * @returns {DocumentFragment}
 */
export function html(strings, ...values) {
  let raw = '';
  for (let i = 0; i < strings.length; i++) {
    raw += strings[i];
    if (i < values.length) {
      const v = values[i];
      if (v && typeof v === 'object' && /** @type {{__raw: string}} */ (v).__raw !== undefined) {
        raw += /** @type {{__raw: string}} */ (v).__raw;
      } else {
        raw += escape(v);
      }
    }
  }
  const template = document.createElement('template');
  template.innerHTML = raw.trim();
  return template.content;
}

/**
 * Marca uma string como HTML pré-validado pra ser interpolado sem escape
 * em html`...`. Use com cuidado — só pra strings que JÁ vieram de fontes
 * confiáveis (ícones SVG, conteúdo gerado internamente).
 *
 * @param {string} str
 * @returns {{__raw: string}}
 */
html.raw = function raw(str) {
  return { __raw: str };
};

/**
 * Substitui o conteúdo do root pelo child. Limpa antes pra evitar leak de
 * listeners do conteúdo anterior (browser GC os trata se não há refs).
 *
 * @param {Element} root
 * @param {Node | DocumentFragment} child
 */
export function mount(root, child) {
  root.innerHTML = '';
  root.appendChild(child);
}

/**
 * Helper pra event delegation — adiciona listener no root mas só dispara
 * quando o target casa o seletor.
 *
 * @param {Element} root
 * @param {string} eventType
 * @param {string} selector
 * @param {(event: Event, matched: Element) => void} handler
 * @returns {() => void} unsubscribe
 */
export function on(root, eventType, selector, handler) {
  /** @param {Event} event */
  const wrapper = (event) => {
    const target = event.target;
    if (!(target instanceof Element)) return;
    const matched = target.closest(selector);
    if (!matched || !root.contains(matched)) return;
    handler(event, matched);
  };
  root.addEventListener(eventType, wrapper);
  return () => root.removeEventListener(eventType, wrapper);
}
