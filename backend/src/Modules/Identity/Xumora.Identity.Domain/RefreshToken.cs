namespace Xumora.Identity.Domain;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(
        string userId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset familyExpiresAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        FamilyExpiresAt = familyExpiresAt;
    }

    public Guid Id { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset FamilyExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public int Version { get; private set; }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now && FamilyExpiresAt > now;

    public void Revoke(DateTimeOffset now, Guid? replacedByTokenId = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        ReplacedByTokenId = replacedByTokenId;
        Version++;
    }
}
