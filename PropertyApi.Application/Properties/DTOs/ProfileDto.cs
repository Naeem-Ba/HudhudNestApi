namespace PropertyApi.Application.Properties.DTOs;

public class ProfileDto
{
    public string? Vorname { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? TaxNumber { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

