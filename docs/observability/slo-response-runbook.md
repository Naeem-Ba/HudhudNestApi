# SLO response runbook

Availability budget is 0.5%, latency budget 5%, and authentication budget 1%
over 30 days. Fast burn (14.4x) pages and freezes risky releases. Slow burn (2x)
creates a reliability work item and blocks discretionary risk until the trend
recovers. Validate the SLI denominator and telemetry freshness before acting.

Prioritize restoration, then reduce load or roll back. Exit a release freeze
only after burn rate is below 1x, telemetry is complete, and the incident owner
confirms recovery. Track reliability work against the consumed error budget.
