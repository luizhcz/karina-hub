-- 007 — drop aihub.users.IsAdmin
--
-- Admin gating passou a vir do header x-efs-permissions (resolvido pelo proxy/IdP)
-- e comparado contra Admin:AdminPermissions em appsettings. A coluna IsAdmin e o
-- índice associado deixam de ser fonte da verdade.

DROP INDEX IF EXISTS aihub."IX_users_TenantId_IsAdmin";
ALTER TABLE aihub.users DROP COLUMN IF EXISTS "IsAdmin";
