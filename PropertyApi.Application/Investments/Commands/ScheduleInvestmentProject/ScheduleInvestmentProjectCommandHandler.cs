using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Commands.ScheduleInvestmentProject;

public sealed class ScheduleInvestmentProjectCommandHandler : IRequestHandler<ScheduleInvestmentProjectCommand>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IUnitOfWork _uow;

    public ScheduleInvestmentProjectCommandHandler(IInvestmentProjectRepository projects, IUnitOfWork uow)
    {
        _projects = projects;
        _uow = uow;
    }

    public async Task Handle(ScheduleInvestmentProjectCommand request, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"Investment project {request.Id} was not found.");

        project.Schedule(request.ScheduledPublishAt);
        await _uow.SaveChangesAsync(ct);
    }
}
