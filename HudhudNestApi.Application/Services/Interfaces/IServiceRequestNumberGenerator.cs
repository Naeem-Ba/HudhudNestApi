namespace HudhudNestApi.Application.Services.Interfaces;

/// <summary>
/// Produces the next server-generated, globally unique ServiceRequest.RequestNumber (e.g.
/// "SR-2026-000001"). Implemented in Infrastructure with a native Postgres sequence
/// (nextval) — concurrency-safe with no advisory lock needed, at the cost of the number never
/// resetting per calendar year (year two starts at SR-2027-000348, not SR-2027-000001).
/// </summary>
public interface IServiceRequestNumberGenerator
{
    Task<string> NextAsync(CancellationToken ct = default);
}
