using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Agencies.DTOs;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Agencies.Mapping;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Application.Agencies.Commands.CreateAgency;

/// <summary>
/// Creates an agency, attaches the caller to it, and grants them the AgencyOwner role.
///
/// All three or none. Half of this is not a usable state: an agency whose owner never got
/// the role has nobody who can add members to it, and a user carrying AgencyOwner with no
/// AgencyId can administer nothing. The database work runs in one transaction; the role
/// grant is discussed below because it does not share that transaction.
/// </summary>
public sealed class CreateAgencyCommandHandler
    : IRequestHandler<CreateAgencyCommand, AgencyDto>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAdminIdentityService _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateAgencyCommandHandler> _logger;

    public CreateAgencyCommandHandler(
        IAgencyRepository agencies,
        IAdminIdentityService identity,
        IUnitOfWork unitOfWork,
        ILogger<CreateAgencyCommandHandler> logger)
    {
        _agencies = agencies;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AgencyDto> Handle(
        CreateAgencyCommand request,
        CancellationToken cancellationToken)
    {
        var account = await _agencies.GetUserAccountAsync(request.RequestingUserId, cancellationToken);

        if (account is null)
            throw new NotFoundException("User account was not found.");

        if (account.AgencyId is not null)
        {
            throw new ConflictException(
                "أنت عضو في مكتب عقاري بالفعل. غادر مكتبك الحالي قبل إنشاء مكتب جديد.");
        }

        var existingOwned = await _agencies.GetByOwnerAsync(request.RequestingUserId, cancellationToken);

        if (existingOwned is not null)
        {
            throw new ConflictException(
                "تملك مكتباً عقارياً بالفعل. لا يمكن امتلاك أكثر من مكتب واحد.");
        }

        // Normalise before the uniqueness check, so "Damascus Homes" and "damascus-homes"
        // cannot both be accepted and then collide on the unique index.
        var slug = Agency.NormalizeSlug(request.Slug);

        if (await _agencies.SlugExistsAsync(slug, cancellationToken))
        {
            throw new ConflictException(
                $"المعرّف المختصر '{slug}' مستخدم من مكتب آخر. اختر معرّفاً مختلفاً.");
        }

        var now = DateTime.UtcNow;

        var agency = Agency.Create(
            name: request.Name,
            slug: slug,
            ownerUserId: request.RequestingUserId,
            countryCode: request.CountryCode,
            utcNow: now);

        agency.UpdateProfile(
            name: request.Name,
            description: request.Description,
            contactEmail: request.ContactEmail,
            contactPhone: request.ContactPhone,
            city: request.City,
            utcNow: now);

        agency.SetLicenseNumber(request.LicenseNumber, now);

        // Structured location — public setters, exactly like Property.GovernorateId/
        // DistrictId/NeighborhoodId. Hierarchy consistency (District belongs to the
        // given Governorate, Neighborhood belongs to the given District) is enforced by
        // CreateAgencyCommandValidator before the handler ever runs, so no re-check here.
        agency.GovernorateId = request.GovernorateId;
        agency.DistrictId = request.DistrictId;
        agency.NeighborhoodId = request.NeighborhoodId;

        agency.CreatedByUserId = request.RequestingUserId;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await _agencies.AddAsync(agency, cancellationToken);

            account.JoinAgency(agency.Id, now);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        // Identity lives in its own store and cannot enlist in the transaction above, so
        // this runs after the commit. If it fails, the agency exists but its owner lacks
        // the role — recoverable by an administrator granting AgencyOwner, and far better
        // than the reverse (a role granted for an agency that was rolled away). The failure
        // is logged as an error rather than swallowed, because it needs a human.
        var roleResult = await _identity.AssignRoleAsync(
            userId: request.RequestingUserId,
            role: RoleNames.AgencyOwner,
            performedByUserId: request.RequestingUserId,
            ipAddress: request.IpAddress,
            ct: cancellationToken);

        if (!roleResult.Succeeded)
        {
            _logger.LogError(
                "Agency {AgencyId} was created but the AgencyOwner role could not be granted to {UserId}: {Message}",
                agency.Id,
                request.RequestingUserId,
                roleResult.Message);
        }

        _logger.LogInformation(
            "Agency created. AgencyId={AgencyId}, Slug={Slug}, OwnerId={OwnerId}",
            agency.Id,
            agency.Slug,
            request.RequestingUserId);

        return AgencyMapper.ToDto(agency, [account], memberCount: 1);
    }
}
