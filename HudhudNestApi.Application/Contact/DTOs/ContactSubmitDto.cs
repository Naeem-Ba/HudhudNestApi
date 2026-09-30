using System.ComponentModel.DataAnnotations;

namespace HudhudNestApi.Application.Contact.DTOs;

public sealed record ContactSubmitDto(
    [Required]
    [StringLength(150, MinimumLength = 2)]
    string Name,

    [Required]
    [EmailAddress]
    [StringLength(320)]
    string Email,

    [StringLength(300)]
    string? Subject,

    [Required]
    [StringLength(5000, MinimumLength = 10)]
    string Message);

