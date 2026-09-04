using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Commands.AddInvestmentUpdate;

public sealed class AddInvestmentUpdateCommandHandler : IRequestHandler<AddInvestmentUpdateCommand, Guid>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IInvestmentUpdateRepository _updates;
    private readonly IUnitOfWork _uow;

    public AddInvestmentUpdateCommandHandler(
        IInvestmentProjectRepository projects,
        IInvestmentUpdateRepository updates,
        IUnitOfWork uow)
    {
        _projects = projects;
        _updates = updates;
        _uow = uow;
    }

    public async Task<Guid> Handle(AddInvestmentUpdateCommand request, CancellationToken ct)
    {
        var projectExists = await _projects.GetByIdAsync(request.InvestmentProjectId, ct) is not null;
        if (!projectExists)
            throw new NotFoundException($"Investment project {request.InvestmentProjectId} was not found.");

        var update = InvestmentUpdate.Create(
            request.InvestmentProjectId,
            request.Title,
            request.Content,
            request.UpdateType,
            request.CreatedByUserId);

        _updates.Add(update);
        await _uow.SaveChangesAsync(ct);

        return update.Id;
    }
}
