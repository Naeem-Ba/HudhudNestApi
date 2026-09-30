using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.ScheduleInvestmentProject;

public sealed record ScheduleInvestmentProjectCommand(Guid Id, DateTime? ScheduledPublishAt) : IRequest;
