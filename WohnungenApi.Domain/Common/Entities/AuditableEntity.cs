using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WohnungenApi.Domain.Common.Entities;

/// <summary>
/// Extends BaseEntity with WHO created/updated/deleted the record.
/// Use this for all entities that require a full audit trail.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public Guid? DeletedByUserId { get; set; }
}