using Microsoft.EntityFrameworkCore;
using WohnungenApi.Data;
using WohnungenApi.Dtos;
using WohnungenApi.Models;

namespace WohnungenApi.Services
{
    public class WohnungService : IWohnungService
    {
        private readonly WohnungenContext _context;
        private readonly IPhotoService _photoService;

        public WohnungService(
            WohnungenContext context,
            IPhotoService photoService)
        {
            _context = context;
            _photoService = photoService;
        }

        // ============================================
        // CREATE
        // ============================================
        public async Task<Wohnung> CreateWohnungAsync(
            WohnungCreateDto dto,
            int userId)
        {
            var wohnung = new Wohnung
            {
                Titel = dto.Titel,
                Beschreibung = dto.Beschreibung,
                Adresse = dto.Adresse,
                Stadt = dto.Stadt,
                PLZ = dto.PLZ,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                ZumMieten = dto.ZumMieten,
                ZumKaufen = dto.ZumKaufen,
                Kaltmiete = dto.Kaltmiete,
                Warmmiete = dto.Warmmiete,
                Kaufpreis = dto.Kaufpreis,
                Nebenkosten = dto.Nebenkosten,
                Kaution = dto.Kaution,
                Zimmer = dto.Zimmer,
                Flaeche = dto.Flaeche,
                Geschoss = dto.Geschoss,
                FreiAb = dto.FreiAb,
                Balkon = dto.Balkon,
                Aufzug = dto.Aufzug,
                Stellplatz = dto.Stellplatz,
                Heizung = dto.Heizung,
                Energieausweis = dto.Energieausweis,
                Zustand = dto.Zustand,
                Status = dto.Status,
                OwnerId = userId,
                ExpiresAt = DateTime.UtcNow.AddMonths(3),
                Bilder = new List<Wohnungsbild>()
            };

            if (dto.Bilder != null)
            {
                foreach (var file in dto.Bilder)
                {
                    if (file.Length == 0) continue;

                    var result = await _photoService.AddPhotoAsync(file);

                    if (result.Error != null)
                        throw new Exception(result.Error.Message);

                    wohnung.Bilder.Add(new Wohnungsbild
                    {
                        Url = result.SecureUrl.AbsoluteUri,
                        PublicId = result.PublicId,
                        IsMain = wohnung.Bilder.Count == 0
                    });
                }
            }

            _context.Wohnungen.Add(wohnung);
            await _context.SaveChangesAsync();

            return wohnung;
        }

        // ============================================
        // GET ALL
        // ============================================
        public async Task<IEnumerable<Wohnung>> GetAllAsync()
        {
            return await _context.Wohnungen
                .Include(w => w.Bilder)
                .AsNoTracking()
                .ToListAsync();
        }

        // ============================================
        // GET MINE
        // ============================================
        public async Task<IEnumerable<Wohnung>> GetMineAsync(int userId)
        {
            return await _context.Wohnungen
                .Where(w => w.OwnerId == userId)
                .Include(w => w.Bilder)
                .OrderByDescending(w => w.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        // ============================================
        // GET BY ID
        // ============================================
        public async Task<Wohnung?> GetByIdAsync(int id)
        {
            return await _context.Wohnungen
                .Include(w => w.Bilder)
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == id);
        }

        // ============================================
        // UPDATE
        // ============================================
        public async Task<Wohnung?> UpdateWohnungAsync(
            int id,
            WohnungUpdateDto dto,
            int userId)
        {
            var wohnung = await _context.Wohnungen
                .Include(w => w.Bilder)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (wohnung == null)
                return null;

            if (wohnung.OwnerId != userId)
                throw new UnauthorizedAccessException();

            wohnung.Titel = dto.Titel;
            wohnung.Beschreibung = dto.Beschreibung;
            wohnung.ZumMieten = dto.ZumMieten;
            wohnung.ZumKaufen = dto.ZumKaufen;
            wohnung.Kaltmiete = dto.Kaltmiete;
            wohnung.Warmmiete = dto.Warmmiete;
            wohnung.Kaufpreis = dto.Kaufpreis;
            wohnung.Nebenkosten = dto.Nebenkosten;
            wohnung.Kaution = dto.Kaution;
            wohnung.Zimmer = dto.Zimmer;
            wohnung.Flaeche = dto.Flaeche;
            wohnung.Geschoss = dto.Geschoss;
            wohnung.FreiAb = dto.FreiAb;
            wohnung.Balkon = dto.Balkon;
            wohnung.Aufzug = dto.Aufzug;
            wohnung.Stellplatz = dto.Stellplatz;
            wohnung.Heizung = dto.Heizung;
            wohnung.Energieausweis = dto.Energieausweis;
            wohnung.Zustand = dto.Zustand;
            wohnung.Status = dto.Status;

            if (dto.Bilder != null)
            {
                foreach (var file in dto.Bilder)
                {
                    if (file.Length == 0) continue;

                    var result = await _photoService.AddPhotoAsync(file);
                    if (result.Error != null) continue;

                    wohnung.Bilder.Add(new Wohnungsbild
                    {
                        Url = result.SecureUrl.AbsoluteUri,
                        PublicId = result.PublicId,
                        IsMain = false
                    });
                }
            }

            await _context.SaveChangesAsync();
            return wohnung;
        }

        // ============================================
        // DELETE
        // ============================================
        public async Task<bool> DeleteWohnungAsync(int id, int userId)
        {
            var wohnung = await _context.Wohnungen
                .Include(w => w.Bilder)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (wohnung == null)
                return false;

            if (wohnung.OwnerId != userId)
                throw new UnauthorizedAccessException();

            // حذف الصور من Cloudinary
            foreach (var bild in wohnung.Bilder)
            {
                if (!string.IsNullOrEmpty(bild.PublicId))
                {
                    await _photoService.DeletePhotoAsync(bild.PublicId);
                }
            }

            _context.Wohnungen.Remove(wohnung);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}