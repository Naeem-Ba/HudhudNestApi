using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace PropertyApi.Application.Common.Interfaces;

/// <summary>
/// Minimal read-only property accessor.
/// Keeps booking and review handlers independent of the full property repository.
/// </summary>
public interface IPropertyReadRepository
{
    Task<PropertySummary?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Lightweight DTO returned by IPropertyReadRepository.</summary>
public sealed record PropertySummary(
    Guid Id,
    string Title,
    string City,
    Guid OwnerId,
    bool IsPublished,
    string? MainImageUrl
);