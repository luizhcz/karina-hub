/// <reference types="vite/client" />

interface ImportMetaEnv {
  /**
   * Lista CSV de permissions injetada em `x-efs-permissions` quando rodando
   * em dev local sem o proxy de produção. Usada apenas como fallback quando
   * `identity.permissions` está vazia. Ex.: `efs.admin,efs.cliente`.
   */
  readonly VITE_DEV_PERMISSIONS?: string

  /**
   * Origem do backend (host + porta, sem path). O path `/api/aihub` é
   * fixo no cliente e concatenado automaticamente. Ex.:
   * `https://api.efs.com`. Vazio = path relativo `/api/aihub` (proxy do
   * Vite em dev local).
   */
  readonly VITE_API_HOST?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
