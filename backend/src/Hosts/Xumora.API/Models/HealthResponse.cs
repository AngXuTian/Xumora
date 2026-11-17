using System.Text.Json.Serialization;

namespace Xumora.API.Models;

public sealed record HealthResponse(
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Service = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Database = null);
