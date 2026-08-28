using MediatR;
using PropertyApi.Application.Services.DTOs;

namespace PropertyApi.Application.Services.Commands.CreateServiceProvider;

/// <summary>Admin-only — see ServiceProvidersController. Creates the marketplace-facing
/// business profile for an existing UserAccount.</summary>
public sealed record CreateServiceProviderCommand(
    Guid UserId,
    Guid? AgencyId,
    string DisplayName,
    string? Bio,
    string? ContactEmail,
    string? ContactPhone) : IRequest<ServiceProviderDto>;
