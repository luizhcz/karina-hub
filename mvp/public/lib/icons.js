// @ts-check
/**
 * Biblioteca de ícones SVG inline (strings). Substitui mvp/src/ui/Icons.tsx.
 * Cada ícone é uma função que recebe className opcional e retorna string SVG.
 *
 * Uso:
 *   import { ToolIcon } from './icons.js';
 *   element.innerHTML = ToolIcon('h-4 w-4');
 *   // ou em html`...`:
 *   html`${html.raw(ToolIcon('h-4 w-4'))}`;
 *
 * Adicionar novos ícones aqui conforme necessário — preserve a base
 * stroke=currentColor pra herdar a cor do contexto.
 */

const BASE_ATTRS =
  'fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" viewBox="0 0 24 24"';

/**
 * @param {string} [className]
 * @param {number} [strokeWidth]
 * @returns {string}
 */
function open(className, strokeWidth = 1.7) {
  return `<svg ${BASE_ATTRS} stroke-width="${strokeWidth}"${className ? ` class="${className}"` : ''}>`;
}

/** @param {string} [className] */
export function ToolIcon(className) {
  return `${open(className)}<path d="M14.7 6.3a3 3 0 0 0 4 4l3-3a5 5 0 0 1-7 7L7 21a2.1 2.1 0 0 1-3-3l7.7-7.7a5 5 0 0 1 7-7l-3 3Z" /></svg>`;
}

/** @param {string} [className] */
export function PlusIcon(className) {
  return `${open(className, 2)}<path d="M12 5v14M5 12h14" /></svg>`;
}

/** @param {string} [className] */
export function SearchIcon(className) {
  return `${open(className)}<circle cx="11" cy="11" r="7" /><path d="m20 20-3.5-3.5" /></svg>`;
}

/** @param {string} [className] */
export function ChevronDownIcon(className) {
  return `${open(className, 2)}<path d="m6 9 6 6 6-6" /></svg>`;
}

/** @param {string} [className] */
export function CloseIcon(className) {
  return `${open(className, 2)}<path d="m6 6 12 12M6 18 18 6" /></svg>`;
}

/** @param {string} [className] */
export function SettingsIcon(className) {
  return `${open(className)}<circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" /></svg>`;
}

/** @param {string} [className] */
export function SunIcon(className) {
  return `${open(className)}<circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M5 5l1.4 1.4M17.6 17.6 19 19M2 12h2M20 12h2M5 19l1.4-1.4M17.6 6.4 19 5" /></svg>`;
}

/** @param {string} [className] */
export function MoonIcon(className) {
  return `${open(className)}<path d="M21 12.8A8.5 8.5 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" /></svg>`;
}

/** @param {string} [className] */
export function ArrowLeftIcon(className) {
  return `${open(className, 2)}<path d="M19 12H5M12 19l-7-7 7-7" /></svg>`;
}

/** @param {string} [className] */
export function ArrowRightIcon(className) {
  return `${open(className, 2)}<path d="M5 12h14M12 5l7 7-7 7" /></svg>`;
}

/** @param {string} [className] */
export function CheckIcon(className) {
  return `${open(className, 2.2)}<path d="m5 12 5 5L20 7" /></svg>`;
}

/** @param {string} [className] */
export function LogoIcon(className) {
  return `${open(className, 2)}<path d="M5 8h14M5 12h10M5 16h12" /></svg>`;
}

/** @param {string} [className] */
export function ServerIcon(className) {
  return `${open(className)}<rect x="3" y="4" width="18" height="6" rx="2" /><rect x="3" y="14" width="18" height="6" rx="2" /><path d="M7 7h.01M7 17h.01" /></svg>`;
}

/** @param {string} [className] */
export function PlugIcon(className) {
  return `${open(className)}<path d="M9 2v6M15 2v6M5 8h14v3a7 7 0 0 1-14 0V8ZM12 22v-7" /></svg>`;
}

/** @param {string} [className] */
export function AgentIcon(className) {
  return `${open(className)}<rect x="4" y="7" width="16" height="12" rx="3" /><path d="M12 3v4M9 13h.01M15 13h.01M9 17h6" /><path d="M2 13h2M20 13h2" /></svg>`;
}

/** @param {string} [className] */
export function BoltIcon(className) {
  return `${open(className)}<path d="M13 2 4 14h7l-1 8 9-12h-7l1-8z" /></svg>`;
}

/** @param {string} [className] */
export function SparklesIcon(className) {
  return `${open(className)}<path d="M12 3v4M12 17v4M3 12h4M17 12h4" /><path d="m6 6 2.5 2.5M15.5 15.5 18 18M6 18l2.5-2.5M15.5 8.5 18 6" /></svg>`;
}

/** @param {string} [className] */
export function ChartIcon(className) {
  return `${open(className)}<path d="M3 3v17a1 1 0 0 0 1 1h17" /><path d="M7 16l4-4 4 4 5-5" /></svg>`;
}
