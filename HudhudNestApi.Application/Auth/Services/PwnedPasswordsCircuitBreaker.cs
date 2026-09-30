namespace HudhudNestApi.Application.Auth.Services;

/// <summary>
/// Stops HudhudNestApi from hammering api.pwnedpasswords.com once it has clearly
/// stopped answering.
///
/// Breach screening fails open by design: a Have I Been Pwned outage must not block
/// registration. The cost of that is a request that waits for the HTTP timeout and
/// then accepts the password anyway. Under a sustained outage every registration pays
/// that latency for a check that cannot succeed. After <see cref="FailureThreshold"/>
/// consecutive failures the circuit opens and calls are skipped outright for
/// <see cref="OpenDuration"/>, then one probe is allowed through to test recovery.
///
/// Registered as a singleton: the state is shared across requests, which is the whole
/// point. Scoped services cannot see each other's failures.
/// </summary>
public sealed class PwnedPasswordsCircuitBreaker
{
    public const int FailureThreshold = 5;
    public static readonly TimeSpan OpenDuration = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTimeOffset? _openedAtUtc;

    public PwnedPasswordsCircuitBreaker(TimeProvider timeProvider)
        => _timeProvider = timeProvider;

    /// <summary>
    /// False when the circuit is open and the cooldown has not elapsed, in which case
    /// the caller must skip the network call entirely.
    /// </summary>
    public bool ShouldAttempt()
    {
        lock (_gate)
        {
            if (_openedAtUtc is not { } openedAt)
            {
                return true;
            }

            if (_timeProvider.GetUtcNow() - openedAt < OpenDuration)
            {
                return false;
            }

            // Cooldown elapsed: half-open. Let one call through. If it fails,
            // RecordFailure re-opens the circuit for another full cooldown.
            _openedAtUtc = null;
            _consecutiveFailures = FailureThreshold - 1;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openedAtUtc = null;
        }
    }

    /// <summary>Returns true when this failure is the one that opened the circuit.</summary>
    public bool RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;

            if (_consecutiveFailures < FailureThreshold || _openedAtUtc is not null)
            {
                return false;
            }

            _openedAtUtc = _timeProvider.GetUtcNow();
            return true;
        }
    }
}
