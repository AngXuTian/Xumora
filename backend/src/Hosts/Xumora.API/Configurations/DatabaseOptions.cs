using System.ComponentModel.DataAnnotations;

namespace Xumora.API.Configurations;

public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    [Required]
    public string DefaultConnection { get; set; } = string.Empty;
}
