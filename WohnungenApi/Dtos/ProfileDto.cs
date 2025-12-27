namespace WohnungenApi.Dtos
{
    public class ProfileDto
    {
        public string? Vorname { get; set; }
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? Phone { get; set; }
        public bool? IsAgent { get; set; }
        public string TaxNumber { get; set; }
    }
}
