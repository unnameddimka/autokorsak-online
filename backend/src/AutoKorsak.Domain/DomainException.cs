namespace AutoKorsak.Domain;

/// <summary>
/// A business-rule violation. <see cref="Code"/> is a stable, language-neutral key that the UI
/// translates; <see cref="Args"/> supplies values for the localized message.
/// </summary>
public class DomainException(string code, IReadOnlyDictionary<string, object?>? args = null, bool isConflict = false)
    : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?> Args { get; } = args ?? new Dictionary<string, object?>();

    /// <summary>True when the request is valid but needs explicit confirmation or conflicts with existing data (HTTP 409).</summary>
    public bool IsConflict { get; } = isConflict;

    public static DomainException Conflict(string code, IReadOnlyDictionary<string, object?>? args = null) =>
        new(code, args, isConflict: true);
}

public sealed class NotFoundException(string code) : DomainException(code);
