using WohnungenApi.Models;

namespace WohnungenApi.Dtos
{
    public class BenutzerDto
    {

            public string DisplayName { get; set; } = string.Empty;
            public bool? IsAgent { get; set; }
            public string? Name { get; set; }
            public string? Vorname { get; set; }
            public string Email { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public string PasswordHash { get; set; } = string.Empty;
       
    }
}
