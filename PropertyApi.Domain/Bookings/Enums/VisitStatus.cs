
namespace PropertyApi.Domain.Bookings.Enums;

public enum VisitStatus
{
    Pending = 0,
    Confirmed = 1,
    Declined = 2,
    Cancelled = 3,
    Completed = 4,

    /// <summary>
    /// The owner proposed a different date/time than the one the requester originally asked
    /// for. Awaiting the requester's response via AcceptReschedule/DeclineReschedule.
    /// </summary>
    RescheduleProposed = 5,
}
