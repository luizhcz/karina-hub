using System.Text.RegularExpressions;

namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Helpers estáticos de validação compartilhados por AgentService e
/// WorkflowService para eliminar duplicação de mensagens e regras de formato.
/// Operam sobre <see cref="List{String}"/> para manter compatibilidade com
/// os validadores existentes.
/// </summary>
public static class ValidationContext
{
    private static readonly Regex IdentifierRegex =
        new(@"^[A-Za-z0-9_\-]+$", RegexOptions.Compiled);

    /// <summary>Retorna true se o campo estiver preenchido.</summary>
    public static bool RequireNotEmpty(List<string> errors, string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Campo '{fieldName}' é obrigatório.");
            return false;
        }
        return true;
    }

    public static void RequireMaxLength(List<string> errors, string? value, int maxLength, string fieldName)
    {
        if (value is not null && value.Length > maxLength)
            errors.Add($"Campo '{fieldName}' não pode ultrapassar {maxLength} caracteres.");
    }

    /// <summary>
    /// Valida um identificador (obrigatório, tamanho máximo e formato `^[A-Za-z0-9_-]+$`).
    /// </summary>
    public static void RequireIdentifier(List<string> errors, string? value, string fieldName, int maxLength = 100)
    {
        if (!RequireNotEmpty(errors, value, fieldName)) return;

        RequireMaxLength(errors, value, maxLength, fieldName);

        if (!IdentifierRegex.IsMatch(value!))
            errors.Add($"Campo '{fieldName}' deve conter apenas letras, números, hífens e underscores (^[A-Za-z0-9_-]+$).");
    }

    /// <summary>Valida obrigatório + tamanho máximo em um único chamado.</summary>
    public static void RequireString(List<string> errors, string? value, string fieldName, int maxLength)
    {
        if (RequireNotEmpty(errors, value, fieldName))
            RequireMaxLength(errors, value, maxLength, fieldName);
    }
}
