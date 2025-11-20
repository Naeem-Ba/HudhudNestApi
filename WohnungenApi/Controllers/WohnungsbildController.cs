using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Models;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WohnungsbildController : ControllerBase
    {
        private readonly WohnungenContext _context;
        private readonly IWebHostEnvironment _env;

        public WohnungsbildController(WohnungenContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [HttpPost("{wohnungId}")]
        [RequestSizeLimit(10_000_000)] // 10 MB
        public async Task<IActionResult> UploadBild(int wohnungId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Keine Datei hochgeladen.");

            var wohnung = await _context.Wohnungen.FindAsync(wohnungId);
            if (wohnung == null)
                return NotFound("Wohnung nicht gefunden.");

            var uploadsPath = Path.Combine(_env.WebRootPath, "bilder");
            if (!Directory.Exists(uploadsPath))
                Directory.CreateDirectory(uploadsPath);

            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(uploadsPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var url = $"{Request.Scheme}://{Request.Host}/bilder/{fileName}";

            var bild = new Wohnungsbild
            {
                WohnungId = wohnungId,
                Url = url,
                IsMain = false
            };

            _context.Wohnungsbilder.Add(bild);
            await _context.SaveChangesAsync();

            return Ok(bild);
        }

        [HttpGet("{wohnungId}")]
        public IActionResult GetBilder(int wohnungId)
        {
            var bilder = _context.Wohnungsbilder.Where(b => b.WohnungId == wohnungId).ToList();
            return Ok(bilder);
        }
    }
}
