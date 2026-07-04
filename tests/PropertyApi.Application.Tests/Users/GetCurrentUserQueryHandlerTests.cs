using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Queries.GetCurrentUser;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Tests.Users;

public sealed class GetCurrentUserQueryHandlerTests
{
    [Fact]
    public async Task Handle_CombinesDomainProfileWithIdentitySnapshot()
    {
        var id = Guid.NewGuid();
        var user = CreateUser(id);
        var identity = new IdentityAccountSnapshot(
            id, id, "user@example.com", "+491234", true, true, true);
        var sut = new GetCurrentUserQueryHandler(
            new StubIdentityService(identity, ["User"]),
            new StubUserRepository(user));

        var result = await sut.Handle(new GetCurrentUserQuery(id), default);

        Assert.NotNull(result);
        Assert.Equal("user@example.com", result!.Email);
        Assert.Equal("Naeem", result.FirstName);
        Assert.True(result.EmailConfirmed);
        Assert.Contains("User", result.Roles);
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenDomainUserIsDeleted()
    {
        var id = Guid.NewGuid();
        var user = CreateUser(id);
        user.IsDeleted = true;
        var sut = new GetCurrentUserQueryHandler(
            new StubIdentityService(null, []),
            new StubUserRepository(user));

        var result = await sut.Handle(new GetCurrentUserQuery(id), default);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenIdentityAccountIsMissing()
    {
        var id = Guid.NewGuid();
        var sut = new GetCurrentUserQueryHandler(
            new StubIdentityService(null, []),
            new StubUserRepository(CreateUser(id)));

        var result = await sut.Handle(new GetCurrentUserQuery(id), default);

        Assert.Null(result);
    }

    private static User CreateUser(Guid id) => new()
    {
        Id = id,
        FirstName = "Naeem",
        LastName = "User",
        CreatedAt = DateTime.UtcNow
    };

    private sealed class StubIdentityService(
        IdentityAccountSnapshot? account,
        IReadOnlyList<string> roles) : IPureIdentityService
    {
        public Task<IdentityAccountSnapshot?> FindByIdAsync(Guid identityId, CancellationToken ct = default)
            => Task.FromResult(account);

        public Task<IReadOnlyList<string>> GetRolesAsync(Guid identityId, CancellationToken ct = default)
            => Task.FromResult(roles);

        public Task<IdentityAccountSnapshot?> FindByEmailAsync(string email, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IdentityOperationResult> CreateAsync(CreateIdentityAccount request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CheckPasswordAsync(Guid identityId, string password, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IdentityOperationResult> AddToRoleAsync(Guid identityId, string role, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StubUserRepository(User? user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(user);

        public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<User>> GetAllActiveAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
