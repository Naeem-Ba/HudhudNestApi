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
        public async Task<IEnumerable<Wohnung>> Get()
        {
            return await _context.Wohnungen.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Wohnung>> GetById(int id)
        {
            var wohnung = await _context.Wohnungen.FindAsync(id);
            if (wohnung == null) return NotFound();
            return wohnung;
        }
    }
}