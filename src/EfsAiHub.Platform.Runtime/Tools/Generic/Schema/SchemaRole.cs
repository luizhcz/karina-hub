namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Papel do schema dentro do tool — controla decisões finas da normalização
/// (ex.: input schema flat para FormUrlEncoded; output schema com array root
/// quando CSV é a content type).
/// </summary>
public enum SchemaRole
{
    Input,
    Output,
}
