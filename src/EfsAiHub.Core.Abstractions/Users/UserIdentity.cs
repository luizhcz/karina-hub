namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Identidade externa do caller, antes da persistência. <see cref="ExternalUserId"/>
/// é o valor opaco vindo da fonte de identidade (header hoje, claim sub do
/// access_token amanhã). <see cref="UserType"/> classifica a origem ("cliente"
/// ou "admin") e permanece como dimensão informativa em audit — não decide
/// gating (quem decide é User.IsAdmin no diretório).
/// </summary>
public sealed record UserIdentity(string ExternalUserId, string UserType);
