namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Identidade externa do caller, antes da persistência. <see cref="ExternalUserId"/>
/// é o valor opaco vindo da fonte de identidade (header hoje, claim sub do
/// access_token amanhã). <see cref="UserType"/> classifica a origem ("cliente"
/// ou "admin") e permanece como dimensão informativa em audit. <see cref="Permissions"/>
/// carrega as permissões enviadas pelo proxy/IdP — gating de admin é derivado
/// dessa lista por <c>AdminPermissionEvaluator</c>; o tipo em si não decide nada.
/// Lista normalizada (lowercase, trim, dedupe, sem vazios).
/// </summary>
public sealed record UserIdentity(
    string ExternalUserId,
    string UserType,
    IReadOnlyList<string> Permissions);
