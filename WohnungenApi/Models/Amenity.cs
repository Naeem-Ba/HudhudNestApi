using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace WohnungenApi.Models
{
    public class Amenity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ICollection<Wohnung> Wohnungen { get; set; } = new List<Wohnung>();

    }
  

}
