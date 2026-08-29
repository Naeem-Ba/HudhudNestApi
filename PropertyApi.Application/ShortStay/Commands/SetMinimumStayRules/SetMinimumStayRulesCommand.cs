using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Commands.SetMinimumStayRules;

public sealed record MinimumStayRuleInput(int MinimumNights, DateOnly? DateRangeStart, DateOnly? DateRangeEnd);

public sealed record SetMinimumStayRulesCommand(
    Guid RoomTypeId,
    Guid OwnerId,
    IReadOnlyList<MinimumStayRuleInput> Rules) : IRequest<bool>;

public sealed class SetMinimumStayRulesCommandHandler : IRequestHandler<SetMinimumStayRulesCommand, bool>
{
    private readonly IRoomTypeRepository _roomTypes;
    private readonly IMinimumStayRuleRepository _minimumStayRules;

    public SetMinimumStayRulesCommandHandler(IRoomTypeRepository roomTypes, IMinimumStayRuleRepository minimumStayRules)
    {
        _roomTypes = roomTypes;
        _minimumStayRules = minimumStayRules;
    }

    public async Task<bool> Handle(SetMinimumStayRulesCommand request, CancellationToken ct)
    {
        var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct)
            ?? throw new NotFoundException($"RoomType {request.RoomTypeId} was not found.");

        if (roomType.ShortStayListing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can set minimum-stay rules.");

        var rules = request.Rules.Select(r => new MinimumStayRule
        {
            RoomTypeId = roomType.Id,
            MinimumNights = r.MinimumNights,
            DateRangeStart = r.DateRangeStart,
            DateRangeEnd = r.DateRangeEnd,
        }).ToList();

        await _minimumStayRules.ReplaceForRoomTypeAsync(roomType.Id, rules, ct);
        return true;
    }
}
