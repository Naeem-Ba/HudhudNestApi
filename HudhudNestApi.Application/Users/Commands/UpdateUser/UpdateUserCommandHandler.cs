using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler
    : IRequestHandler<UpdateUserCommand, UserDto?>
{
    private readonly IUpdateUserIdentityService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        IUpdateUserIdentityService identity,
        IUserAccountRepository accounts,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _identity = identity;
        _accounts = accounts;
        _plans = plans;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<UserDto?> Handle(
        UpdateUserCommand request,
        CancellationToken cancellationToken)
    {
        var identity =
            await _identity.FindByIdAsync(
                request.UserId,
                cancellationToken);

        if (identity is null ||
            identity.IsDeleted)
        {
            return null;
        }

        var account =
            await _accounts.GetByIdAsync(
                identity.UserAccountId,
                cancellationToken);

        if (account is null)
        {
            _logger.LogWarning(
                "UserAccount profile was not found for identity {IdentityId}.",
                identity.IdentityId);

            return null;
        }

        var roles =
            await _identity.GetRolesAsync(
                identity.IdentityId,
                cancellationToken);

        var isAgent =
            roles.Any(
                role =>
                    string.Equals(
                        role,
                        RoleNames.Agent,
                        StringComparison.OrdinalIgnoreCase));

        var effectivePhoneNumber =
            request.PhoneNumber is null
                ? identity.PhoneNumber
                : NormalizeNullable(
                    request.PhoneNumber);

        if (isAgent &&
            string.IsNullOrWhiteSpace(
                effectivePhoneNumber))
        {
            // Was InvalidOperationException, which ExceptionHandlingMiddleware could
            // only turn into a 500. This is the caller's input being wrong, not the
            // server failing, so it belongs in the 422 validation channel.
            throw new ValidationException(
                nameof(UpdateUserCommand.PhoneNumber),
                "PhoneNumber is required for agents.");
        }

        var now =
            DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(
            cancellationToken);

        try
        {
            /*
             * Identity/security state.
             */
            if (request.PhoneNumber is not null)
            {
                var phoneResult =
                    await _identity.SetPhoneNumberAsync(
                        identity.IdentityId,
                        effectivePhoneNumber,
                        now,
                        cancellationToken);

                if (!phoneResult.Succeeded)
                {
                    _logger.LogWarning(
                        "Phone-number update failed for identity {IdentityId}. Errors: {Errors}",
                        identity.IdentityId,
                        string.Join(
                            ", ",
                            phoneResult.Errors));

                    await _unitOfWork.RollbackTransactionAsync(
                        cancellationToken);

                    // Translate the stable codes SetPhoneNumberAsync returns into the
                    // right HTTP channel. Previously every one of these -- including
                    // "that number belongs to someone else" -- became an
                    // InvalidOperationException and reached the caller as a 500.
                    throw phoneResult.Errors.Contains("PHONE_NUMBER_ALREADY_IN_USE")
                        ? new ConflictException(
                            "That phone number is already registered to another account.")
                        : new ValidationException(
                            nameof(UpdateUserCommand.PhoneNumber),
                            phoneResult.Errors.Contains("PHONE_NUMBER_INVALID")
                                ? "Phone number must be in international E.164 format, for example +963911234567."
                                : string.Join(" | ", phoneResult.Errors));
                }
            }

            /*
             * Profile state.
             */
            var effectiveFirstName =
                request.FirstName is null
                    ? account.FirstName
                    : request.FirstName.Trim();

            var effectiveLastName =
                request.LastName is null
                    ? account.LastName
                    : request.LastName.Trim();

            var effectiveDisplayName =
                request.DisplayName is null
                    ? account.DisplayName
                    : NormalizeNullable(
                        request.DisplayName);

            account.UpdateProfile(
                effectiveFirstName,
                effectiveLastName,
                effectiveDisplayName,
                now);

            var effectiveProfileImageUrl =
                request.ProfileImageUrl is null
                    ? account.ProfileImageUrl
                    : NormalizeNullable(
                        request.ProfileImageUrl);

            // ✅ إذا لم يتغيّر رابط الصورة، نحافظ على PublicId الحالي (قد يكون
            // من رفع سابق عبر /me/avatar). أما لو وصل رابط جديد من هذا المسار
            // تحديدًا (نص حر عبر PUT /users/me)، فلا نملك PublicId له — هذا
            // المسار لا يمرّ عبر IMediaStorageService، بعكس UploadUserAvatarCommandHandler.
            var effectiveProfileImagePublicId =
                request.ProfileImageUrl is null
                    ? account.ProfileImagePublicId
                    : null;

            account.UpdateProfileImage(
                effectiveProfileImageUrl,
                effectiveProfileImagePublicId,
                now);

            var effectiveLanguage =
                request.PreferredLanguage is null
                    ? account.PreferredLanguage
                    : request.PreferredLanguage
                        .Trim()
                        .ToLowerInvariant();

            var effectiveCurrency =
                request.PreferredCurrency is null
                    ? account.PreferredCurrency
                    : request.PreferredCurrency
                        .Trim()
                        .ToUpperInvariant();

            var effectiveCountryCode =
                request.CountryCode is null
                    ? account.CountryCode
                    : NormalizeNullable(
                        request.CountryCode)
                        ?.ToUpperInvariant();

            account.UpdatePreferences(
                effectiveLanguage,
                effectiveCurrency,
                effectiveCountryCode,
                now);

            var effectiveBio =
                request.Bio is null
                    ? account.Bio
                    : NormalizeNullable(
                        request.Bio);

            var effectiveContactInfo =
                request.ContactInfo is null
                    ? account.ContactInfo
                    : NormalizeNullable(
                        request.ContactInfo);

            account.UpdateAboutInfo(
                effectiveBio,
                effectiveContactInfo,
                now);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);

            await _unitOfWork.CommitTransactionAsync(
                cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);

            throw;
        }

        /*
         * Reload Identity state because changing the phone number
         * may change PhoneNumber and PhoneConfirmed state.
         */
        var updatedIdentity =
            await _identity.FindByIdAsync(
                identity.IdentityId,
                cancellationToken);

        if (updatedIdentity is null)
        {
            throw new InvalidOperationException(
                $"Identity '{identity.IdentityId}' was not found after update.");
        }

        var planTier =
            account.PlanId is { } planId
                ? (await _plans.GetByIdAsync(planId, cancellationToken))?.Tier
                : null;

        return new UserDto
        {
            Id =
                account.Id,

            Email =
                updatedIdentity.Email
                ?? string.Empty,

            FirstName =
                account.FirstName,

            LastName =
                account.LastName,

            DisplayName =
                account.DisplayName,

            PhoneNumber =
                updatedIdentity.PhoneNumber,

            ProfileImageUrl =
                account.ProfileImageUrl,

            PreferredLanguage =
                account.PreferredLanguage,

            PreferredCurrency =
                account.PreferredCurrency,

            CountryCode =
                account.CountryCode,

            Bio =
                account.Bio,

            ContactInfo =
                account.ContactInfo,

            EmailConfirmed =
                updatedIdentity.EmailConfirmed,

            PhoneConfirmed =
                updatedIdentity.PhoneConfirmed,

            PlanTier =
                planTier,

            PlanSelectedAt =
                account.PlanSelectedAt,

            CreatedAt =
                account.CreatedAt,

            Roles =
                roles
                    .ToList()
                    .AsReadOnly()
        };
    }

    private static string? NormalizeNullable(
        string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
