using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Lookups.Entities;
using Microsoft.EntityFrameworkCore;

namespace PropertyApi.Infrastructure.Persistence.Seeds;

public static class PropertyTypesSeed
{
    public static async Task SeedAsync(AppDbContext context)
    {
        if (await context.PropertyTypes.AnyAsync())
            return;

        var types = new List<PropertyType>
        {
            // Residential
            PropertyType.Create("شقة سكنية",    "Apartment",      "Residential", "apartment",    1),
            PropertyType.Create("شقة فندقية",   "Hotel Apartment", "Residential", "hotel-apt",   2),
            PropertyType.Create("فيلا",         "Villa",          "Residential", "villa",        3),
            PropertyType.Create("منزل مستقل",   "House",          "Residential", "house",        4),
            PropertyType.Create("استوديو",      "Studio",         "Residential", "studio",       5),

            // Land
            PropertyType.Create("أرض سكنية",   "Residential Land", "Land", "land-residential", 10),
            PropertyType.Create("أرض زراعية",   "Agricultural Land","Land", "land-agricultural", 11),
            PropertyType.Create("أرض صناعية",   "Industrial Land", "Land", "land-industrial",  12),
            PropertyType.Create("أرض تجارية",   "Commercial Land", "Land", "land-commercial",  13),

            // Commercial
            PropertyType.Create("محل تجاري",   "Shop",           "Commercial", "shop",          20),
            PropertyType.Create("مكتب",        "Office",         "Commercial", "office",         21),
            PropertyType.Create("مستودع",      "Warehouse",      "Commercial", "warehouse",      22),
            PropertyType.Create("فندق",        "Hotel",          "Commercial", "hotel",          23),
        };

        await context.PropertyTypes.AddRangeAsync(types);
        await context.SaveChangesAsync();
    }
}