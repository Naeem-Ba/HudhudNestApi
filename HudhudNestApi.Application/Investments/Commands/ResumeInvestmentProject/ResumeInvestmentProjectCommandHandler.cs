using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Commands.ResumeInvestmentProject;

public sealed class ResumeInvestmentProjectCommandHandler : IRequestHandler<ResumeInvestmentProjectCommand>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IUnitOfWork _uow;

    public ResumeInvestmentProjectCommandHandler(IInvestmentProjectRepository projects, IUnitOfWork uow)
    {
        _projects = projects;
        _uow = uow;
    }

    public async Task Handle(ResumeInvestmentProjectCommand request, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"Investment project {request.Id} was not found.");

        project.Resume();
        await _uow.SaveChangesAsync(ct);
    }
}
