using WohnungenApi.Dtos;
using WohnungenApi.Models;

namespace WohnungenApi.Services
{
    public interface IWohnungService
    {
        Task<Wohnung> CreateWohnungAsync(WohnungCreateDto dto, int userId);
        Task<IEnumerable<Wohnung>> GetAllAsync();
        Task<IEnumerable<Wohnung>> GetMineAsync(int userId);
        Task<Wohnung?> GetByIdAsync(int id);
        Task<Wohnung?> UpdateWohnungAsync(int id, WohnungUpdateDto dto, int userId);
        Task<bool> DeleteWohnungAsync(int id, int userId);
    }
}