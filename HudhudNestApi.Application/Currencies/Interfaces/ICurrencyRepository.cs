using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Application.Currencies.Interfaces;

public interface ICurrencyRepository
{
    Task<IReadOnlyList<Currency>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Tracked currency by ISO code (active or not), for the administrator's update.</summary>
    Task<Currency?> GetByCodeAsync(string code, CancellationToken ct = default);

    void Add(Currency currency);
}
