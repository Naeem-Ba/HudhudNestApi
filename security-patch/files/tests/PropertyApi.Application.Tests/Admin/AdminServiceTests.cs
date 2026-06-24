using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Admin.Services;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Application.Tests.Admin;

public sealed class AdminServiceTests
{
    [Fact]
    public async Task GetUsersWithPagination_ClampsPageAndPageSize_AndDelegatesToRepository()
    {
        var users = new FakeAdminUserQueryRepository();
        var identity = new FakeAdminIdentityService();
        var service = new AdminService(users, identity);

        await service.GetUsersWithPaginationAsync(
            page: -10,
            pageSize: 500,
            role: "agent",
            CancellationToken.None);

        Assert.Equal(1, users.LastPage);
        Assert.Equal(100, users.LastPageSize);
        Assert.Equal(RoleNames.Agent, users.LastRole);
    }

    [Fact]
    public void GetRoles_ReturnsAllowedRoleNames()
    {
        var service = new AdminService(
            new FakeAdminUserQueryRepository(),
            new FakeAdminIdentityService());

        var roles = service.GetRoles();

        Assert.Contains(RoleNames.User, roles);
        Assert.Contains(RoleNames.Agent, roles);
        Assert.Contains(RoleNames.Admin, roles);
    }

    [Fact]
    public async Task SetUserRole_InvalidRole_ReturnsInvalidRole_AndDoesNotCallIdentity()
    {
        var identity = new FakeAdminIdentityService();
        var service = new AdminService(
            new FakeAdminUserQueryRepository(),
            identity);

        var result = await service.SetUserRoleAsync(
            Guid.NewGuid(),
            "SuperAdmin",
            Guid.NewGuid(),
            "127.0.0.1",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("The requested role is invalid.", result.Message);
        Assert.Equal(0, identity.SetRoleCalls);
    }

    [Fact]
    public async Task SetUserRole_NormalizesRole_AndDelegatesToIdentity()
    {
        var identity = new FakeAdminIdentityService();
        var service = new AdminService(
            new FakeAdminUserQueryRepository(),
            identity);

        var userId = Guid.NewGuid();

        var actorId = Guid.NewGuid();

        var result = await service.SetUserRoleAsync(
            userId,
            "aGeNt",
            actorId,
            "127.0.0.1",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(userId, identity.LastUserId);
        Assert.Equal(RoleNames.Agent, identity.LastRole);
        Assert.Equal(1, identity.SetRoleCalls);
    }

    [Fact]
    public async Task AssignRole_InvalidRole_ReturnsInvalidRole_AndDoesNotCallIdentity()
    {
        var identity = new FakeAdminIdentityService();
        var service = new AdminService(
            new FakeAdminUserQueryRepository(),
            identity);

        var result = await service.AssignRoleAsync(
            Guid.NewGuid(),
            "Invalid",
            Guid.NewGuid(),
            "127.0.0.1",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("The requested role is invalid.", result.Message);
        Assert.Equal(0, identity.AssignRoleCalls);
    }

    [Fact]
    public async Task RemoveRole_NormalizesRole_AndDelegatesToIdentity()
    {
        var identity = new FakeAdminIdentityService();
        var service = new AdminService(
            new FakeAdminUserQueryRepository(),
            identity);

        var userId = Guid.NewGuid();

        var actorId = Guid.NewGuid();

        var result = await service.RemoveRoleAsync(
            userId,
            "USER",
            actorId,
            "127.0.0.1",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(userId, identity.LastUserId);
        Assert.Equal(RoleNames.User, identity.LastRole);
        Assert.Equal(1, identity.RemoveRoleCalls);
    }

    [Fact]
    public async Task DisableUser_DelegatesToIdentity()
    {
        var identity = new FakeAdminIdentityService();
        var service = new AdminService(
            new FakeAdminUserQueryRepository(),
            identity);

        var userId = Guid.NewGuid();

        var result = await service.DisableUserAsync(userId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(userId, identity.LastUserId);
        Assert.Equal(1, identity.DisableCalls);
    }

    private sealed class FakeAdminUserQueryRepository : IAdminUserQueryRepository
    {
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        public string? LastRole { get; private set; }

        public Task<PagedResult<AdminUserDto>> GetUsersWithPaginationAsync(
            int page,
            int pageSize,
            string? role,
            CancellationToken ct = default)
        {
            LastPage = page;
            LastPageSize = pageSize;
            LastRole = role;

            return Task.FromResult(new PagedResult<AdminUserDto>
            {
                Items = Array.Empty<AdminUserDto>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize
            });
        }
    }

    private sealed class FakeAdminIdentityService : IAdminIdentityService
    {
        public Guid LastUserId { get; private set; }
        public string? LastRole { get; private set; }
        public int SetRoleCalls { get; private set; }
        public int AssignRoleCalls { get; private set; }
        public int RemoveRoleCalls { get; private set; }
        public int DisableCalls { get; private set; }

        public Task<AdminOperationResult> SetSingleRoleAsync(
            Guid userId,
            string role,
            Guid performedByUserId,
            string? ipAddress,
            CancellationToken ct = default)
        {
            LastUserId = userId;
            LastRole = role;
            SetRoleCalls++;
            return Task.FromResult(AdminOperationResult.Ok("Role updated."));
        }

        public Task<AdminOperationResult> AssignRoleAsync(
            Guid userId,
            string role,
            Guid performedByUserId,
            string? ipAddress,
            CancellationToken ct = default)
        {
            LastUserId = userId;
            LastRole = role;
            AssignRoleCalls++;
            return Task.FromResult(AdminOperationResult.Ok("Role assigned."));
        }

        public Task<AdminOperationResult> RemoveRoleAsync(
            Guid userId,
            string role,
            Guid performedByUserId,
            string? ipAddress,
            CancellationToken ct = default)
        {
            LastUserId = userId;
            LastRole = role;
            RemoveRoleCalls++;
            return Task.FromResult(AdminOperationResult.Ok("Role removed."));
        }

        public Task<AdminOperationResult> DisableUserAsync(
            Guid userId,
            CancellationToken ct = default)
        {
            LastUserId = userId;
            DisableCalls++;
            return Task.FromResult(AdminOperationResult.Ok("User disabled."));
        }
    }
}
