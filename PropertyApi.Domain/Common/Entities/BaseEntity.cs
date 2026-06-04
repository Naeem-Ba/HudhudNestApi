using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Domain.Common.Entities;

/// <summary>
/// Base class for ALL domain entities.
/// Guid PK — safe for distributed systems, no sequential guessing.
/// All timestamps in UTC — required for global multi-timezone support.
/// Soft delete built-in — never lose data, support GDPR erasure requests.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Soft Delete — never hard-delete records
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
