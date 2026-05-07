// @ts-check
/**
 * Observer pattern minimalista pra state management. Substitui Zustand /
 * useReducer / Context API com ~30 linhas. Persistência opcional em
 * localStorage por chave.
 *
 * Pattern: createStore<T>(initialValue, storageKey?) → { get, set, patch,
 * subscribe }. Subscriber é chamado a cada set/patch com o valor novo.
 *
 * @template T
 * @param {T} initialValue
 * @param {string} [storageKey]
 */
export function createStore(initialValue, storageKey) {
  /** @type {Set<(v: T) => void>} */
  const listeners = new Set();

  /** @returns {T} */
  const readInitial = () => {
    if (!storageKey || typeof window === 'undefined') return initialValue;
    try {
      const raw = window.localStorage.getItem(storageKey);
      if (!raw) return initialValue;
      const parsed = JSON.parse(raw);
      return parsed ?? initialValue;
    } catch {
      return initialValue;
    }
  };

  let value = readInitial();

  const persist = () => {
    if (!storageKey || typeof window === 'undefined') return;
    if (value === null || value === undefined) {
      window.localStorage.removeItem(storageKey);
    } else {
      window.localStorage.setItem(storageKey, JSON.stringify(value));
    }
  };

  const notify = () => {
    listeners.forEach((cb) => cb(value));
  };

  return {
    /** @returns {T} */
    get: () => value,

    /** @param {T} next */
    set(next) {
      value = next;
      persist();
      notify();
    },

    /** @param {Partial<T>} delta */
    patch(delta) {
      if (value === null || value === undefined) return;
      // Só permite patch quando T é um objeto. Em outros casos, use set().
      value = /** @type {T} */ ({ ...(/** @type {object} */ (value)), ...delta });
      persist();
      notify();
    },

    /**
     * @param {(v: T) => void} cb
     * @returns {() => void} unsubscribe
     */
    subscribe(cb) {
      listeners.add(cb);
      return () => {
        listeners.delete(cb);
      };
    },
  };
}
