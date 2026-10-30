using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.UpdateInvestmentProgress;

/// <summary>Staff-entered progress figure for a project past the editing stage (informational only; never derived
/// from a payment).</summary>
public sealed record UpdateInvestmentProgressCommand(Guid Id, decimal RaisedAmount) : IRequest;
