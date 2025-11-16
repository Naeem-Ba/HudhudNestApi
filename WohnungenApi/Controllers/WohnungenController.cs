using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Models;
using Microsoft.EntityFrameworkCore;


namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WohnungenController : ControllerBase
    {
        private readonly WohnungenContext _context;

        public WohnungenController(WohnungenContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateWohnung([FromBody] Wohnung wohnung)
        {
            try
            {
                _context.Wohnungen.Add(wohnung);
                await _context.SaveChangesAsync();
                return Ok(wohnung);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString()); // يعطيك نص الاستثناء بالكامل
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Wohnung>>> Get()
        {
            var wohnungen = await _context.Wohnungen
                .Include(w => w.Bilder) 
                .ToListAsync();
            return Ok(wohnungen);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Wohnung>> GetById(int id)
        {
            var wohnung = await _context.Wohnungen
                .Include(w => w.Bilder)
                .FirstOrDefaultAsync(w => w.Id == id);
            if (wohnung == null) return NotFound();
            return wohnung;
        }
    }
}