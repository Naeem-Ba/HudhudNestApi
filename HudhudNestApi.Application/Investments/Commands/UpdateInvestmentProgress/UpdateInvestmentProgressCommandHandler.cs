using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Commands.UpdateInvestmentProgress;

public sealed class UpdateInvestmentProgressCommandHandler : IRequestHandler<UpdateInvestmentProgressCommand>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IUnitOfWork _uow;

    public UpdateInvestmentProgressCommandHandler(IInvestmentProjectRepository projects, IUnitOfWork uow)
    {
        _projects = projects;
        _uow = uow;
    }

    public async Task Handle(UpdateInvestmentProgressCommand request, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"Investment project {request.Id} was not found.");

        project.UpdateProgress(request.RaisedAmount);
        await _uow.SaveChangesAsync(ct);
    }
}
