namespace HudhudNestApi.Domain.Demand.Enums;

/// <summary>Lifecycle of a <see cref="Entities.PropertySearchRequest"/>. Stored as text.</summary>
public enum SearchRequestStatus
{
    Active = 1,
    Paused = 2,
    Closed = 3,
    Expired = 4,
}
