namespace HudhudNestApi.Application.Growth;

/// <summary>Where a new user came from (UTM tags or a referral link). Tags only, never free text.</summary>
public sealed record AcquisitionTags(string? Source, string? Medium, string? Campaign);

/// <summary>
/// Growth engine P4: writes one row to the growth event log. Idempotent by key, never throws: a failed
/// analytics write must not fail the user's action. Call it after the action has been saved (and after a
/// transaction is committed), never inside an open transaction.
/// </summary>
public interface IGrowthEventRecorder
{
    Task RecordAsync(
        string name,
        string idempotencyKey,
        Guid? userId = null,
        Guid? propertyId = null,
        Guid? subjectId = null,
        AcquisitionTags? tags = null,
        CancellationToken ct = default);
}

/// <summary>For unit tests and composition roots that do not record growth events.</summary>
public sealed class NullGrowthEventRecorder : IGrowthEventRecorder
{
    public static readonly NullGrowthEventRecorder Instance = new();

    public Task RecordAsync(string name, string idempotencyKey, Guid? userId = null, Guid? propertyId = null,
        Guid? subjectId = null, AcquisitionTags? tags = null, CancellationToken ct = default) => Task.CompletedTask;
}
