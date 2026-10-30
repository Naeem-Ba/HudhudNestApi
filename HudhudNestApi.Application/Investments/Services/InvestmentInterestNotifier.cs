using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Services;

/// <summary>
/// QA Phase 10, P10-D01: an expression of interest used to reach nobody. This tells the admins a new one arrived
/// (project title and the number of people interested so far; never the person's name, which is on the project's
/// interest list). Called after the interest is committed and never throws: a notice that fails is logged and the
/// interest stays saved.
/// </summary>
public sealed class InvestmentInterestNotifier
{
    private readonly INotificationService _notifications;
    private readonly IInvestmentInterestRepository _interests;
    private readonly ILogger<InvestmentInterestNotifier> _logger;

    public InvestmentInterestNotifier(
        INotificationService notifications,
        IInvestmentInterestRepository interests,
        ILogger<InvestmentInterestNotifier> logger)
    {
        _notifications = notifications;
        _interests = interests;
        _logger = logger;
    }

    public async Task NotifyAdminsAsync(InvestmentInterest interest, string projectTitle, CancellationToken ct)
    {
        try
        {
            var adminIds = await _interests.ListAdminIdsAsync(ct);
            if (adminIds.Count == 0)
                return;

            var count = await _interests.CountActiveAsync(interest.InvestmentProjectId, ct);
            var message = NotificationText.Of(NotificationKeys.InvestmentInterestReceived,
                ("title", NotificationText.FromLiteral(projectTitle)),
                ("count", count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            // A retry of the same expression is a no-op; a fresh one after a withdrawal (new UpdatedAt) is new.
            var dedupeKey = $"iin:{interest.Id:N}:{interest.UpdatedAt.Ticks}";

            foreach (var adminId in adminIds.Where(id => id != interest.UserId))
            {
                try
                {
                    await _notifications.NotifyInvestmentInterestAsync(
                        adminId, message, interest.InvestmentProjectId, dedupeKey, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "Investment interest notification failed. ProjectId={ProjectId}, AdminId={AdminId}",
                        interest.InvestmentProjectId, adminId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Could not notify admins of a new investment interest. ProjectId={ProjectId}", interest.InvestmentProjectId);
        }
    }
}
