using MediatR;

namespace PropertyApi.Application.Search.Commands.DeleteSavedSearch;

public sealed record DeleteSavedSearchCommand(Guid Id, Guid UserId) : IRequest<bool>;
