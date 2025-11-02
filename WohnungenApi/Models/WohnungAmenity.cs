using System.ComponentModel.DataAnnotations;

namespace WohnungenApi.Models
{
    public class WohnungAmenity
    {
        [Key]
        public int Id { get; set; }
        public int WohnungId { get; set; }
        public Wohnung? Wohnung { get; set; }
        public int AmenityId { get; set; }
        public Amenity? Amenity { get; set; }
    }
}
