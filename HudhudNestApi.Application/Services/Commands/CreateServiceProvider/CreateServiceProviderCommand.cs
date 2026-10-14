using MediatR;
using HudhudNestApi.Application.Services.DTOs;

namespace HudhudNestApi.Application.Services.Commands.CreateServiceProvider;

/// <summary>Admin-only — see ServiceProvidersController. Creates the marketplace-facing
/// business profile for an existing UserAccount directly, without an application. The normal
/// route is now approving a ServiceProviderApplication; without approved categories a profile
/// created here cannot publish offerings until an admin sets its scope.</summary>
public sealed record CreateServiceProviderCommand(
    Guid UserId,
    Guid? AgencyId,
    string DisplayName,
    string? Bio,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<Domain.Services.Enums.ServiceCategory>? ApprovedCategories = null,
    IReadOnlyList<int>? CoverageGovernorateIds = null) : IRequest<ServiceProviderDto>;
