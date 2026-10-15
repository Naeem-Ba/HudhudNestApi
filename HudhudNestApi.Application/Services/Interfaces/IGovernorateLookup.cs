namespace HudhudNestApi.Application.Services.Interfaces;

/// <summary>Validates coverage areas against the Governorates lookup instead of trusting client ids.</summary>
public interface IGovernorateLookup
{
    /// <summary>The subset of <paramref name="ids"/> that are real, active governorates.</summary>
    Task<IReadOnlyList<int>> GetKnownIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);
}
