using MediatR;
using HudhudNestApi.Application.Contact.DTOs;

namespace HudhudNestApi.Application.Contact.Queries.GetContactMessages;

public sealed record GetContactMessagesQuery(
    int Page,
    int PageSize) : IRequest<ContactMessagesPageDto>;

