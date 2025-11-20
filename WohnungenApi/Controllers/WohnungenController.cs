using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Models;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Dtos;



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
        public async Task<IActionResult> CreateWohnung([FromForm] WohnungCreateDto dto)
        {
            var wohnung = new Wohnung
            {
                Titel = dto.Titel,
                Beschreibung = dto.Beschreibung,
                Adresse = dto.Adresse,
                ZumMieten = dto.ZumMieten,
                Kaltmiete = dto.Kaltmiete,
                Kaution = dto.Kaution,
                Zimmer = dto.Zimmer,
                Flaeche = dto.Flaeche,
                Bilder = new List<Wohnungsbild>()
            };

            if (dto.Bilder != null)
            {
                foreach (var file in dto.Bilder)
                {
                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var filePath = Path.Combine("wwwroot/bilder", fileName);

                    if (!Directory.Exists(Path.Combine("wwwroot", "bilder")))
                        Directory.CreateDirectory(Path.Combine("wwwroot", "bilder"));

                    using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);

                    wohnung.Bilder.Add(new Wohnungsbild
                    {
                        Url = $"{Request.Scheme}://{Request.Host}/bilder/{fileName}",
                        IsMain = false
                    });
                }
            }

            _context.Wohnungen.Add(wohnung);
            await _context.SaveChangesAsync();

            return Ok(wohnung);
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