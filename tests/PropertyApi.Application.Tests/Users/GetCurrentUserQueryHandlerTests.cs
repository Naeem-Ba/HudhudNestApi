using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Queries.GetCurrentUser;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Tests.Users;

public sealed class GetCurrentUserQueryHandlerTests
{
    [Fact]
    public async Task Handle_CombinesUserAccountProfileWithIdentitySnapshot()
    {
        // Arrange
        var id =
            Guid.NewGuid();

        var account =
            CreateUserAccount(id);

        var identity =
            new IdentityAccountSnapshot(
                IdentityId: id,
                UserAccountId: id,
                Email: "user@example.com",
                PhoneNumber: "+49123456789",
                EmailConfirmed: true,
                PhoneConfirmed: true,
                HasPassword: true,
                IsDeleted: false);

        var sut =
            new GetCurrentUserQueryHandler(
                new StubIdentityService(
                    identity,
                    ["User"]),
                new StubUserAccountRepository(
                    account));

        // Act
        var result =
            await sut.Handle(
                new GetCurrentUserQuery(id),
                CancellationToken.None);

        // Assert
        Assert.NotNull(result);

        Assert.Equal(
            id,
            result!.Id);

        Assert.Equal(
            "user@example.com",
            result.Email);

        Assert.Equal(
            "Naeem",
            result.FirstName);

        Assert.Equal(
            "User",
            result.LastName);

        Assert.Equal(
            "Naeem User",
            result.DisplayName);

        Assert.Equal(
            "+49123456789",
            result.PhoneNumber);

        Assert.True(
            result.EmailConfirmed);

        Assert.Contains(
            "User",
            result.Roles);
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenIdentityAccountIsDeleted()
    {
        // Arrange
        var id =
            Guid.NewGuid();

        var identity =
            new IdentityAccountSnapshot(
                IdentityId: id,
                UserAccountId: id,
                Email: "deleted@example.com",
                PhoneNumber: null,
                EmailConfirmed: true,
                PhoneConfirmed: false,
                HasPassword: true,
                IsDeleted: true);

        var sut =
            new GetCurrentUserQueryHandler(
                new StubIdentityService(
                    identity,
                    ["User"]),
                new StubUserAccountRepository(
                    CreateUserAccount(id)));

        // Act
        var result =
            await sut.Handle(
                new GetCurrentUserQuery(id),
                CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenIdentityAccountIsMissing()
    {
        // Arrange
        var id =
            Guid.NewGuid();

        var sut =
            new GetCurrentUserQueryHandler(
                new StubIdentityService(
                    account: null,
                    roles: []),
                new StubUserAccountRepository(
                    CreateUserAccount(id)));

        // Act
        var result =
            await sut.Handle(
                new GetCurrentUserQuery(id),
                CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenUserAccountIsMissing()
    {
        // Arrange
        var id =
            Guid.NewGuid();

        var identity =
            new IdentityAccountSnapshot(
                IdentityId: id,
                UserAccountId: id,
                Email: "user@example.com",
                PhoneNumber: null,
                EmailConfirmed: true,
                PhoneConfirmed: false,
                HasPassword: true,
                IsDeleted: false);

        var sut =
            new GetCurrentUserQueryHandler(
                new StubIdentityService(
                    identity,
                    ["User"]),
                new StubUserAccountRepository(
                    null));

        // Act
        var result =
            await sut.Handle(
                new GetCurrentUserQuery(id),
                CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    private static UserAccount CreateUserAccount(
        Guid id)
    {
        var now =
            DateTime.UtcNow;

        var account =
            UserAccount.Create(
                id,
                "Naeem",
                "User",
                now);

        account.UpdateProfile(
            "Naeem",
            "User",
            "Naeem User",
            now);

        return account;
    }

    private sealed class StubIdentityService(
        IdentityAccountSnapshot? account,
        IReadOnlyList<string> roles)
        : IUserIdentityReadService
    {
        public Task<IdentityAccountSnapshot?>
            FindByIdAsync(
                Guid identityId,
                CancellationToken ct = default)
        {
            if (account is null ||
                account.IdentityId != identityId)
            {
                return Task.FromResult<
                    IdentityAccountSnapshot?>(
                        null);
            }

            return Task.FromResult<
                IdentityAccountSnapshot?>(
                    account);
        }

        public Task<IReadOnlyList<string>>
            GetRolesAsync(
                Guid identityId,
                CancellationToken ct = default)
        {
            return Task.FromResult(
                roles);
        }
    }

    private sealed class StubUserAccountRepository(
        UserAccount? account)
        : IUserAccountRepository
    {
        public Task<UserAccount?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default)
        {
            if (account is null ||
                account.Id != id)
            {
                return Task.FromResult<
                    UserAccount?>(
                        null);
            }

            return Task.FromResult<
                UserAccount?>(
                    account);
        }

        public Task AddAsync(
            UserAccount newAccount,
            CancellationToken ct = default)
        {
            throw new NotSupportedException(
                "This test repository is read-only.");
        }
    }
}
