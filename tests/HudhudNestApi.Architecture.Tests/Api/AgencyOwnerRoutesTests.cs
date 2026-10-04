using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Architecture.Tests.Api;

/// <summary>
/// AgenciesController's owner-only routes must not gate on the AgencyOwner role.
///
/// Roles ride inside the access token, so the role gate turned every freshly created or
/// freshly transferred agency into 403s until the owner's token was refreshed. It also
/// protected nothing: each handler compares the caller to the agency's OwnerUserId
/// (ADR-006), which is the real check. This test keeps the role gate from coming back.
/// </summary>
public sealed class AgencyOwnerRoutesTests
{
    [Fact(DisplayName = "AgenciesController actions authorize by ownership in the handler, not by a role")]
    public void AgenciesController_Should_Not_Gate_On_Roles()
    {
        var controller = typeof(AgenciesController);
        var attributes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(controller
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)));

        var withRoles = attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.Roles)).ToList();

        Assert.True(
            withRoles.Count == 0,
            "AgenciesController must use plain [Authorize] and let the handler compare the caller " +
            "to OwnerUserId; a role gate reads stale roles from the access token. Found: " +
            string.Join(", ", withRoles.Select(attribute => attribute.Roles)));
    }
}
