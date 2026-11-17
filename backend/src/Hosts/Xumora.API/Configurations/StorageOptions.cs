using System.ComponentModel.DataAnnotations;

namespace Xumora.API.Configurations;

public class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string Provider { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;
}