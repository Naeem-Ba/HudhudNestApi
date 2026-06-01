using Microsoft.EntityFrameworkCore;
using WohnungenApi.Application.Users.Interfaces;
using WohnungenApi.Domain.Users.Entities;
using WohnungenApi.Infrastructure.Persistence;

namespace WohnungenApi.Infrastructure.Repositories
{
    /// <summary>
    /// User read operations. Write/management goes through Identity's UserManager&lt;User&gt;.
    ///
    /// NEW FILE: Didn't exist.
    ///
    /// Register in DI:
    ///   builder.Services.AddScoped&lt;IUserRepository, UserRepository&gt;();
    /// </summary>
    public sealed class UserRepository : IUserRepository
    {
        private readonly AppDbContext _db;

        public UserRepository(AppDbContext db) => _db = db;

        public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id, ct);

        public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
            => await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), ct);

        public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
            => await _db.Users.AnyAsync(u => u.Id == id, ct);
    }
}
