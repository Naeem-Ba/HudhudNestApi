namespace HudhudNestApi.Application.Agencies.DTOs;

public enum SetAgencyLogoStatus
{
    Success,
    NotFound,
    Forbidden,
    ValidationFailed,
    StorageFailed
}

/// <summary>
/// Same pattern as UploadUserAvatarResult/PropertyImageMutationResult — an explicit Status
/// instead of throwing for "expected" outcomes (wrong caller, bad file, storage failure),
/// leaving exceptions for genuinely unexpected errors. Unlike the other Agency commands
/// (which throw NotFoundException/ForbiddenException because their only failure modes are
/// domain/authorization checks), this command also has a real external-storage failure
/// mode, which is exactly what the Result-status convention exists for elsewhere in this
/// codebase.
/// </summary>
public sealed record SetAgencyLogoResult(
    SetAgencyLogoStatus Status,
    AgencyDto? Agency = null,
    string? Message = null)
{
    public static SetAgencyLogoResult Success(AgencyDto agency) =>
        new(SetAgencyLogoStatus.Success, agency);

    public static SetAgencyLogoResult NotFound(string? message = null) =>
        new(SetAgencyLogoStatus.NotFound, null, message);

    public static SetAgencyLogoResult Forbidden(string? message = null) =>
        new(SetAgencyLogoStatus.Forbidden, null, message);

    public static SetAgencyLogoResult ValidationFailed(string message) =>
        new(SetAgencyLogoStatus.ValidationFailed, null, message);

    public static SetAgencyLogoResult StorageFailed(string? message) =>
        new(SetAgencyLogoStatus.StorageFailed, null, message);
}
