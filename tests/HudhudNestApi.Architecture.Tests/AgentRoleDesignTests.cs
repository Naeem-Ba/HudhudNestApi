using HudhudNestApi.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Architecture.Tests;

public sealed class AgentRoleDesignTests
{
    [Fact]
    public void UserEntity_Should_Not_Contain_IsAgent_BooleanFlag()
    {
        var property = typeof(ApplicationUser).GetProperty("IsAgent");

        Assert.Null(property);
    }

    [Theory]
    [InlineData(typeof(UserDto))]
    [InlineData(typeof(UserSummaryDto))]
    [InlineData(typeof(ProfileDto))]
    [InlineData(typeof(AdminUserDto))]
    public void PublicDtos_Should_Not_Expose_IsAgent_BooleanFlag(Type dtoType)
    {
        var property = dtoType.GetProperty("IsAgent");

        Assert.Null(property);
    }

    [Fact]
    public void RoleNames_Should_Define_AgentRole()
    {
        Assert.Equal("Agent", RoleNames.Agent);
        Assert.Contains(RoleNames.Agent, RoleNames.All);
    }

    [Fact]
    public void AgentAuthorizationPolicy_Should_Be_RoleBased()
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireRole(RoleNames.Agent)
            .Build();

        var roleRequirement = Assert.Single(
            policy.Requirements.OfType<RolesAuthorizationRequirement>());

        Assert.Contains(RoleNames.Agent, roleRequirement.AllowedRoles);
    }
}
