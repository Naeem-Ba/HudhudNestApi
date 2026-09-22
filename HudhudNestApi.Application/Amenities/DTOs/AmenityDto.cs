namespace HudhudNestApi.Application.Amenities.DTOs;

public sealed class AmenityDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? IconName { get; init; }
}

