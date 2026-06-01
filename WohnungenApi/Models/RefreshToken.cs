namespace WohnungenApi.Models
{
    public class RefreshToken
    {
        public int Id { get; set; }

        public string Token { get; set; } = string.Empty;

        public DateTime Expires { get; set; }

        public bool IsRevoked { get; set; } = false;

        public int UserId { get; set; }

        public Benutzer User { get; set; } = null!;
    }
}