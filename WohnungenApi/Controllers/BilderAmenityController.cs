using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Data;
using WohnungenApi.Models;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BilderAmenityController : ControllerBase
    {
        private readonly WohnungenContext _context;

        public BilderAmenityController(WohnungenContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.Wohnungen
                .Include(w => w.Bilder)
                .Include(w => w.WohnungAmenities)
                    .ThenInclude(wa => wa.Amenity)
                .ToListAsync();
            return Ok(data);
        }

    }
}
