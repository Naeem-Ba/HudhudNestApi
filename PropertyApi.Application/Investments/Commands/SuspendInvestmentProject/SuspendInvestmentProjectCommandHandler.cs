using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Commands.SuspendInvestmentProject;

public sealed class SuspendInvestmentProjectCommandHandler : IRequestHandler<SuspendInvestmentProjectCommand>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IUnitOfWork _uow;

    public SuspendInvestmentProjectCommandHandler(IInvestmentProjectRepository projects, IUnitOfWork uow)
    {
        _projects = projects;
        _uow = uow;
    }

    public async Task Handle(SuspendInvestmentProjectCommand request, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"Investment project {request.Id} was not found.");

        project.Suspend();
        await _uow.SaveChangesAsync(ct);
    }
}
