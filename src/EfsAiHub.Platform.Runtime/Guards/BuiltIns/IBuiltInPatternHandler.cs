namespace EfsAiHub.Platform.Runtime.Guards.BuiltIns;

/// <summary>
/// Handler dinâmico para patterns do tipo <c>builtin</c> no catálogo.
/// O <c>Pattern</c> da row no DB é o <see cref="Id"/> do handler (ex: "internal_tools");
/// o engine resolve esse ID para o handler registrado e materializa <see cref="Literals"/>
/// em runtime (ex: nomes do IFunctionToolRegistry).
///
/// Materialização lazy — a lista é re-snapshotada toda vez que o engine recompila o matcher
/// (raro: cache miss ou após NOTIFY 'blocklist_changed').
/// </summary>
public interface IBuiltInPatternHandler
{
    string Id { get; }
    IReadOnlyCollection<string> Literals { get; }
}
