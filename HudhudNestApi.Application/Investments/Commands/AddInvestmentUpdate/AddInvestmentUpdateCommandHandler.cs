using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.Commands.AddInvestmentUpdate;

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
        var project = await _projects.GetByIdAsync(request.InvestmentProjectId, ct)
            ?? throw new NotFoundException($"Investment project {request.InvestmentProjectId} was not found.");

        // QA Phase 10, P10-D10: a closed project is finished; nothing new is announced on it.
        if (project.Status == InvestmentProjectStatus.Closed)
            throw new DomainException("لا يمكن إضافة تحديث لمشروع مغلق.");

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
