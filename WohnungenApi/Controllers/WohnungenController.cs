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
//        private static readonly List<Wohnung> Wohnungen = new()
//        {
//            new Wohnung { Id = 1, Titel = "Moderne Wohnung in Berlin", Beschreibung = "3 Zimmer, Balkon", Preis = 1200 },
//            new Wohnung { Id = 2, Titel = "Gemütliche Wohnung in Hamburg", Beschreibung = "2 Zimmer, zentral gelegen", Preis = 900 },
//            new Wohnung { Id = 3, Titel = "Schöne Wohnung im Zentrum in Berlin", Beschreibung = "3 Zimmer", Preis = 1250 },
//            new Wohnung { Id = 4, Titel = "Gemütliches Appartement in Hamburg", Beschreibung =  "2 Zimmer", Preis = 800 },
//            new Wohnung { Id = 5, Titel = "Luxuswohnung am See in München", Beschreibung = "4 Zimmer", Preis = 1500 }
//        };

//        [HttpGet]
//        public IEnumerable<Wohnung> Get() => Wohnungen;

//        [HttpGet("{id}")]
//        public ActionResult<Wohnung> GetById(int id)
//        {
//            var wohnung = Wohnungen.FirstOrDefault(w => w.Id == id);
//            if (wohnung == null) return NotFound();
//            return wohnung;
//        }
//    }
//}
