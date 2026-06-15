using MediatR;
using PropertyApi.Application.Contact.DTOs;

namespace PropertyApi.Application.Contact.Queries.GetContactMessageById;

public sealed record GetContactMessageByIdQuery(Guid Id) : IRequest<ContactMessageDto?>;

