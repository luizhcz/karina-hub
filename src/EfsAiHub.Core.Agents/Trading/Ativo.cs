namespace EfsAiHub.Core.Agents.Trading;

public class Ativo
{
    public string Ticker { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Setor { get; set; }
    public string? Descricao { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
