using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Users.Commands.RecordConsent;

public sealed class RecordConsentCommandHandler
    : IRequestHandler<RecordConsentCommand, ConsentRecordDto>
{
    private readonly IConsentRecordRepository _consents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public RecordConsentCommandHandler(
        IConsentRecordRepository consents,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _consents = consents;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<ConsentRecordDto> Handle(
        RecordConsentCommand request,
        CancellationToken cancellationToken)
    {
        // Idempotent: a retried "record consent" call (double-submit, a flaky network
        // retry) for the exact same version the caller already actively consented to
        // must not create a second, redundant row — it returns the existing one instead.
        var existing = await _consents.GetActiveAsync(
            request.UserId,
            request.PolicyType,
            request.PolicyVersion,
            cancellationToken);

        if (existing is not null)
        {
            return ToDto(existing);
        }

        var record = ConsentRecord.Create(
            request.UserId,
            request.PolicyType,
            request.PolicyVersion,
            request.Source,
            _clock.GetUtcNow().UtcDateTime);

        _consents.Add(record);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(record);
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
