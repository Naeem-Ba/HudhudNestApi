namespace HudhudNestApi.Application.Properties.DTOs;

public class ProfileDto
{
    public string? firstName { get; set; }
    public string? lastName { get; set; }
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? TaxNumber { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

