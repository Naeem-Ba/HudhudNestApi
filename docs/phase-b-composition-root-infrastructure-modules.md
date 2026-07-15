# Phase B Composition Root Infrastructure Modules

## Purpose

This slice keeps `AddInfrastructure` as the composition coordinator and moves
capability registrations into focused infrastructure modules.

## Modules

| Module | Responsibility |
| --- | --- |
| `AuthInfrastructureRegistration` | JWT, identity capability adapters, auth repositories, OTP, email, SMS |
| `PersistenceInfrastructureRegistration` | `AppDbContext`, ASP.NET Core Identity stores, Unit of Work |
| `RepositoryInfrastructureRegistration` | Application repository/service adapters for listings, users, notifications, admin, analytics, visits, reviews |
| `UserContextInfrastructureRegistration` | HTTP context access and current user adapter |
| `MediaInfrastructureRegistration` | Cloudinary options, typed HTTP client, media storage adapter |
| `CacheInfrastructureRegistration` | Redis or in-memory distributed cache selection |
| `HealthInfrastructureRegistration` | PostGIS/cache health checks and production startup validation |

## Preserved Behaviors

- PostgreSQL connection string normalization and production validation moved to
  `PostgresConnectionStringResolver`.
- Redis remains required by default in staging and production.
- Testing and CI environments continue to use distributed memory cache when no
  Redis connection is configured.
- Phone-only accounts remain supported by setting `RequireUniqueEmail = false`.
- Cloudinary continues to use the typed `HttpClient` registration.
- Cloudinary options now fail fast when required credentials are missing.
- Cookie CSRF options now validate the authentication cookie name when CSRF is
  enabled.
- Data Protection key storage remains in the root composition path because it is
  a host-level startup concern and can be backed by either PostgreSQL or the
  filesystem.

## Safety Net

`CompositionRegistrationTests` verifies the critical scoped/singleton lifetimes
for the extracted capabilities. Production hardening tests now inspect the
owning modules instead of assuming all registrations live in `DependencyInjection.cs`.
Middleware order and options validation guard tests cover the remaining
composition-root risks.
