using MediatR;

namespace PropertyApi.Application.Investments.Commands.ScheduleInvestmentProject;

public sealed record ScheduleInvestmentProjectCommand(Guid Id, DateTime? ScheduledPublishAt) : IRequest;
