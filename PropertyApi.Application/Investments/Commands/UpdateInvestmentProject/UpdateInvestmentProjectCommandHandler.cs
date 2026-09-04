using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Commands.UpdateInvestmentProject;

public sealed class UpdateInvestmentProjectCommandHandler : IRequestHandler<UpdateInvestmentProjectCommand>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IUnitOfWork _uow;

    public UpdateInvestmentProjectCommandHandler(IInvestmentProjectRepository projects, IUnitOfWork uow)
    {
        _projects = projects;
        _uow = uow;
    }

    public async Task Handle(UpdateInvestmentProjectCommand request, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"Investment project {request.Id} was not found.");

        project.UpdateContent(request.Title, request.ShortDescription, request.Description, request.ProjectType);

        project.UpdateInvestmentParameters(
            request.TargetAmount,
            request.MinimumInvestment,
            request.MaximumInvestment,
            request.Currency,
            request.InvestmentTermMonths,
            request.ExpectedReturnMin,
            request.ExpectedReturnMax);

        project.UpdateSchedule(request.StartDate, request.EndDate);
        project.UpdateRaisedAmount(request.RaisedAmount);

        await _uow.SaveChangesAsync(ct);
    }
}
