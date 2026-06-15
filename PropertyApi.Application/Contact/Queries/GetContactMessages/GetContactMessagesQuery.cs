using MediatR;
using PropertyApi.Application.Contact.DTOs;

namespace PropertyApi.Application.Contact.Queries.GetContactMessages;

public sealed record GetContactMessagesQuery(
    int Page,
    int PageSize) : IRequest<ContactMessagesPageDto>;

