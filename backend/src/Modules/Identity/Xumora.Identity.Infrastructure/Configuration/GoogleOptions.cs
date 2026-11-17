namespace Xumora.Identity.Infrastructure.Configuration;

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    public string WebClientId { get; set; } = string.Empty;
}
