namespace HudhudNestApi.Application.Bookings.Interfaces;

/// <summary>
/// Closes the visit requests still open on a listing that has just been deleted, and tells each visitor
/// (QA Phase 8, P8-D08). Before this a Pending or Confirmed visit stayed on the owner's and the
/// visitor's screens for a listing nobody could open any more.
/// </summary>
public interface IListingVisitCleanup
{
    /// <returns>How many visits were cancelled.</returns>
    Task<int> CancelOpenVisitsAsync(Guid propertyId, string propertyTitle, CancellationToken ct = default);
}
