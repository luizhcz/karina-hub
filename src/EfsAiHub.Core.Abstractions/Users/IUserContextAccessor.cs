namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Acesso scoped ao usuário corrente do request. Populado pelo
/// UserProvisioningMiddleware após a identidade ser resolvida e a row
/// upserted no diretório. Null quando o request veio sem identidade
/// (rota pública, healthcheck, etc.) — consumidores devem tratar como
/// "sem usuário" e não como erro.
/// </summary>
public interface IUserContextAccessor
{
    User? Current { get; set; }
}
