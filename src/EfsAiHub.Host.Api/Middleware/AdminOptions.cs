namespace EfsAiHub.Host.Api.Configuration;

public class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Lista de Account IDs com acesso irrestrito à API.
    /// Se vazia, o gate é desabilitado (útil em testes/dev).</summary>
    public List<string> AccountIds { get; set; } = [];
}
