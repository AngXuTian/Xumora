namespace Xumora.Identity.Application;

public sealed record IdentityOperationResult<T>(
    T? Value,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    bool Conflict = false)
{
    public bool Succeeded => Value is not null;

    public static IdentityOperationResult<T> Success(T value) => new(value);

    public static IdentityOperationResult<T> Failure(
        IReadOnlyDictionary<string, string[]> errors,
        bool conflict = false) => new(default, errors, conflict);
}
