namespace EfsAiHub.Core.Abstractions.Secrets;

public abstract record SecretReference
{
    public const string AwsPrefix = "secret://aws/";

    public static SecretReference Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new EmptySecretReference();

        var trimmed = value.Trim();

        if (trimmed.StartsWith(AwsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var identifier = trimmed.Substring(AwsPrefix.Length).Trim();
            if (string.IsNullOrEmpty(identifier))
                return new EmptySecretReference();
            return new AwsSecretReference(identifier);
        }

        return new LiteralSecretReference(trimmed);
    }

    public static bool IsAwsReference(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Trim().StartsWith(AwsPrefix, StringComparison.OrdinalIgnoreCase);
}

public sealed record AwsSecretReference(string Identifier) : SecretReference
{
    public override string ToString() => AwsPrefix + Identifier;
}

public sealed record LiteralSecretReference(string Value) : SecretReference;

public sealed record EmptySecretReference : SecretReference;
