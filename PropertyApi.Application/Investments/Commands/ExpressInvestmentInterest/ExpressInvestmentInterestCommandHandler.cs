using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Commands.ExpressInvestmentInterest;

public sealed class ExpressInvestmentInterestCommandHandler
    : IRequestHandler<ExpressInvestmentInterestCommand, InvestmentInterestMutationResult>
{
    private readonly IInvestmentInterestRepository _interests;
    private readonly IInvestmentProjectRepository _projects;
    private readonly IUnitOfWork _uow;

    public ExpressInvestmentInterestCommandHandler(
        IInvestmentInterestRepository interests,
        IInvestmentProjectRepository projects,
        IUnitOfWork uow)
    {
        _interests = interests;
        _projects = projects;
        _uow = uow;
    }

    public async Task<InvestmentInterestMutationResult> Handle(
        ExpressInvestmentInterestCommand request,
        CancellationToken ct)
    {
        var projectPublished = await _interests.ProjectExistsAndPublishedAsync(request.InvestmentProjectId, ct);
        if (!projectPublished)
            return InvestmentInterestMutationResult.NotFound("Investment project not found or not published.");

        var existing = await _interests.GetAsync(request.UserId, request.InvestmentProjectId, ct);

        if (existing is not null)
        {
            if (existing.Status == InvestmentInterestStatus.Active)
            {
                // Idempotent — pressing the button twice returns the same state, not an error
                // (Phase 1 spec §31).
                return InvestmentInterestMutationResult.Conflict(
                    "Interest already registered.",
                    ToDto(existing));
            }

            existing.Reactivate();
            await _uow.SaveChangesAsync(ct);
            return InvestmentInterestMutationResult.Success(ToDto(existing), "Interest registered successfully.");
        }

        var project = await _projects.GetByIdAsync(request.InvestmentProjectId, ct);
        var interest = InvestmentInterest.Create(request.UserId, request.InvestmentProjectId);
        _interests.Add(interest);
        await _uow.SaveChangesAsync(ct);

        return InvestmentInterestMutationResult.Success(
            ToDto(interest, project?.Title),
            "Interest registered successfully.");
    }

    private static InvestmentInterestDto ToDto(InvestmentInterest interest, string? projectTitle = null) =>
        new(interest.Id, interest.InvestmentProjectId, projectTitle ?? string.Empty, interest.Status, interest.CreatedAt);
}
