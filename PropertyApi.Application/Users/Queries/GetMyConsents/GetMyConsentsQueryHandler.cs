using MediatR;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Queries.GetMyConsents;

public sealed class GetMyConsentsQueryHandler
    : IRequestHandler<GetMyConsentsQuery, IReadOnlyList<ConsentRecordDto>>
{
    private readonly IConsentRecordRepository _consents;

    public GetMyConsentsQueryHandler(IConsentRecordRepository consents)
    {
        _consents = consents;
    }

    public async Task<IReadOnlyList<ConsentRecordDto>> Handle(
        GetMyConsentsQuery request,
        CancellationToken cancellationToken)
    {
        var records = await _consents.GetByUserIdAsync(request.UserId, cancellationToken);

        return records
            .OrderByDescending(r => r.ConsentedAtUtc)
            .Select(ToDto)
            .ToList();
    }

    private static ConsentRecordDto ToDto(ConsentRecord record)
        => new(
            record.Id,
            record.PolicyType.ToString(),
            record.PolicyVersion,
            record.ConsentedAtUtc,
            record.Source.ToString(),
            record.WithdrawnAtUtc,
            record.IsActive);
}
