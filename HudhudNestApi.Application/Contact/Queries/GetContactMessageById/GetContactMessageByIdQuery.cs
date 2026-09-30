using MediatR;
using HudhudNestApi.Application.Contact.DTOs;

namespace HudhudNestApi.Application.Contact.Queries.GetContactMessageById;

public sealed record GetContactMessageByIdQuery(Guid Id) : IRequest<ContactMessageDto?>;

