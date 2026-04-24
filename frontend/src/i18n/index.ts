import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import LanguageDetector from 'i18next-browser-languagedetector'

import ptBrPersona from '../locales/pt-BR/persona.json'
import enUsPersona from '../locales/en-US/persona.json'

/**
 * Setup de i18n. Estratégia:
 *  - Default: pt-BR (mercado primário).
 *  - Resolução: languagedetector (navigator.language / cookie / localStorage),
 *    fallback explicito pra pt-BR quando unsupported.
 *  - Namespace `persona` pra escopar — páginas não-persona continuam com
 *    strings hardcoded até serem migradas.
 *  - Chaves aninhadas preferidas a _flat_ pra separar escopo (ex:
 *    `experiments.table.variant`).
 */
void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      'pt-BR': { persona: ptBrPersona },
      'en-US': { persona: enUsPersona },
    },
    fallbackLng: 'pt-BR',
    supportedLngs: ['pt-BR', 'en-US'],
    defaultNS: 'persona',
    interpolation: {
      escapeValue: false, // React já escapa
    },
  })

export { i18n }
