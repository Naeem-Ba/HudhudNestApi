# Telemetry data classification

## Allowed

Operation names, route templates, HTTP methods, status codes/classes, bounded
outcomes, dependency type, environment, service name/version, trace/span IDs,
and correlation IDs.

## Allowed after normalization

Routes, exception types, external service names, database names, cache key
categories, user agents, IP addresses, identifiers, and file names. They are
excluded by default unless a privacy and cardinality review approves them.

## Sensitive or prohibited

Passwords/hashes, access and refresh tokens, OTPs, authorization/cookie headers,
API keys, connection strings/passwords, Redis secrets/keys, signed/private URLs,
raw phone/email, messages, private contacts, image bytes, request/response bodies,
SQL text/parameters, and exception messages containing payloads.

Redaction occurs at instrumentation, application processor, and collector
processor layers. Logs correlate only by trace ID, span ID, and correlation ID.
