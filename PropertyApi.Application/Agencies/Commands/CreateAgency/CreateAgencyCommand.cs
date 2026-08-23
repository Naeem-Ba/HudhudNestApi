using MediatR;
using PropertyApi.Application.Agencies.DTOs;

namespace PropertyApi.Application.Agencies.Commands.CreateAgency;

/// <summary>
/// Registers a real-estate office and makes the caller its owner.
/// </summary>
public sealed record CreateAgencyCommand(
    string Name,
    string Slug,
    string CountryCode,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? City,

    /// <summary>
    /// Optional licence number, typed by a human. Never inferred, never extracted from an
    /// uploaded document — the architecture review is explicit that legal identifiers must
    /// come from a person and nothing else.
    /// </summary>
    string? LicenseNumber,

    Guid RequestingUserId,
    string? IpAddress) : IRequest<AgencyDto>;
