using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;

namespace HudhudNestApi.Application.Users.Commands.WithdrawConsent;

public sealed class WithdrawConsentCommandHandler
    : IRequestHandler<WithdrawConsentCommand, int>
{
    private readonly IConsentRecordRepository _consents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public WithdrawConsentCommandHandler(
        IConsentRecordRepository consents,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _consents = consents;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<int> Handle(
        WithdrawConsentCommand request,
        CancellationToken cancellationToken)
    {
        var active = await _consents.GetActiveByTypeAsync(
            request.UserId,
            request.PolicyType,
            cancellationToken);

        if (active.Count == 0)
        {
            return 0;
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        foreach (var record in active)
        {
            record.Withdraw(now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return active.Count;
    }
}
