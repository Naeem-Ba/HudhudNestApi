using MediatR;

namespace HudhudNestApi.Application.Search.Commands.DeleteSavedSearch;

public sealed record DeleteSavedSearchCommand(Guid Id, Guid UserId) : IRequest<bool>;
