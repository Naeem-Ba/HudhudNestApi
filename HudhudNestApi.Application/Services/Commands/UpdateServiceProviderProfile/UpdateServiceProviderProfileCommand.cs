using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;

namespace HudhudNestApi.Application.Services.Commands.UpdateServiceProviderProfile;

/// <summary>Self-service — the provider updates their own profile. ActorUserId comes from
/// the authenticated caller, never the client-supplied provider id.</summary>
public sealed record UpdateServiceProviderProfileCommand(
    Guid ActorUserId,
    string DisplayName,
    string? Bio,
    string? ContactEmail,
    string? ContactPhone) : IRequest<ServiceProviderDto>;

public sealed class UpdateServiceProviderProfileCommandHandler
    : IRequestHandler<UpdateServiceProviderProfileCommand, ServiceProviderDto>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceReviewRepository _reviews;
    private readonly IUnitOfWork _uow;

    public UpdateServiceProviderProfileCommandHandler(
        IServiceProviderRepository providers, IServiceReviewRepository reviews, IUnitOfWork uow)
    {
        _providers = providers;
        _reviews = reviews;
        _uow = uow;
    }

    public async Task<ServiceProviderDto> Handle(UpdateServiceProviderProfileCommand request, CancellationToken ct)
    {
        var provider = await _providers.GetByUserIdAsync(request.ActorUserId, ct)
            ?? throw new NotFoundException("لا يوجد ملف مزوّد خدمة لهذا المستخدم.");

        provider.UpdateProfile(
            request.DisplayName, request.Bio, request.ContactEmail, request.ContactPhone, DateTime.UtcNow);

        await _uow.SaveChangesAsync(ct);

        var (average, count) = await _reviews.GetRatingSummaryAsync(provider.Id, ct);
        return ServiceMapper.ToDto(provider, average, count);
    }
}
