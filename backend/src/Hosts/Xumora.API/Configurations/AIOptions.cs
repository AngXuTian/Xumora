using System.ComponentModel.DataAnnotations;

namespace Xumora.API.Configurations;

public class AIOptions
{
    public const string SectionName = "AI";

    [Required]
    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;
}