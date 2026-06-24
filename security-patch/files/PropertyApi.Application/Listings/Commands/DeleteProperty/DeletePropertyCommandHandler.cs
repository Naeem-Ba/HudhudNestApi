using System.Text.Json;
using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Listings.Commands.DeleteProperty;

public sealed class DeletePropertyCommandHandler
    : IRequestHandler<DeletePropertyCommand, bool>
{
    private readonly IPropertyRepository _repo;
    private readonly IPropertyOwnershipService _ownership;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLogs;

    public DeletePropertyCommandHandler(
        IPropertyRepository repo,
        IPropertyOwnershipService ownership,
        IUnitOfWork uow,
        IAuditLogService auditLogs)
    {
        _repo = repo;
        _ownership = ownership;
        _uow = uow;
        _auditLogs = auditLogs;
    }

    public async Task<bool> Handle(
        DeletePropertyCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _ownership.GetOwnedPropertyOrThrowAsync(
            request.PropertyId,
            request.RequestingUserId,
            operation: "delete",
            ct: cancellationToken);

        var oldValue = JsonSerializer.Serialize(new
        {
            propertyId = property.Id,
            property.Title,
            property.OwnerId,
            property.Status,
            property.IsPublished
        });

        property.MarkAsDeleted(request.RequestingUserId);

        _repo.Remove(property);
        await _uow.SaveChangesAsync(cancellationToken);

        await _auditLogs.LogAsync(
            userId: request.RequestingUserId,
            action: AuditActions.DeleteProperty,
            ipAddress: request.IpAddress,
            oldValue: oldValue,
            newValue: JsonSerializer.Serialize(new
            {
                propertyId = property.Id,
                deleted = true,
                deletedAt = property.DeletedAt,
                deletedByUserId = request.RequestingUserId
            }),
            ct: cancellationToken);

        return true;
    }
}
