using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

/// <summary>
/// Native Postgres sequence — see IServiceRequestNumberGenerator's remarks for why this needs
/// no advisory lock: nextval() is itself atomic and never hands out the same value twice, even
/// under heavy concurrent load. The sequence is created directly in the
/// AddServicesMarketplace migration (EF Core migrations have no first-class "add sequence"
/// builder call for a bare CREATE SEQUENCE with this ownership shape, so it's raw SQL there
/// too — same approach the codebase already uses for PostGIS geography columns).
/// </summary>
public sealed class ServiceRequestNumberGenerator : IServiceRequestNumberGenerator
{
    private const string SequenceName = "\"ServiceRequestNumberSeq\"";

    private readonly AppDbContext _db;
    public ServiceRequestNumberGenerator(AppDbContext db) => _db = db;

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var next = await _db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{SequenceName}')")
            .SingleAsync(ct);

        return $"SR-{DateTime.UtcNow.Year}-{next:D6}";
    }
}
