using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Commands.CreateInvestmentProject;

public sealed class CreateInvestmentProjectCommandHandler
    : IRequestHandler<CreateInvestmentProjectCommand, Guid>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IPropertyReadRepository _properties;
    private readonly IUnitOfWork _uow;

    public CreateInvestmentProjectCommandHandler(
        IInvestmentProjectRepository projects,
        IPropertyReadRepository properties,
        IUnitOfWork uow)
    {
        _projects = projects;
        _properties = properties;
        _uow = uow;
    }

    public async Task<Guid> Handle(CreateInvestmentProjectCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct)
            ?? throw new NotFoundException($"Property {request.PropertyId} was not found.");

        var project = InvestmentProject.Create(
            property.Id,
            request.OwnerUserId,
            request.Title,
            request.Description,
            request.ProjectType,
            request.Currency);

        _projects.Add(project);
        await _uow.SaveChangesAsync(ct);

        return project.Id;
    }
}
