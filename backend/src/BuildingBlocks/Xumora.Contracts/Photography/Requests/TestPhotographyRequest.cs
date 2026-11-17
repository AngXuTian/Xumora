
using System.ComponentModel.DataAnnotations;

namespace Xumora.Contracts.Photography.Requests;

public class TestPhotographyRequest
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string Purpose { get; set; } = string.Empty;

    [Range(1, 5)]
    public int NumberOfOptions { get; set; } = 3;
}
