using MediatR;

namespace HudhudNestApi.Application.Listings.Events;

/// <summary>
/// Raised once a <c>Property</c> transitions Unpublished → Published (Phase 4 spec §9). This is
/// the ONLY thing the Listings bounded context tells the outside world — it does not know, and
/// must never know, that SocialDistribution (or anything else) listens for it.
///
/// Implemented as a plain MediatR <see cref="INotification"/> rather than a new event-bus/queue:
/// MediatR is already the CQRS backbone of every command/query in this codebase
/// (<c>HudhudNestApi.Application.DependencyInjection.AddApplication</c> already calls
/// <c>AddMediatR</c> with assembly scanning), and <see cref="INotification"/>/
/// <see cref="INotificationHandler{TNotification}"/> are part of that same package — publishing
/// one costs zero new infrastructure and is auto-discovered exactly like every
/// <c>IRequestHandler</c> already is. This is deliberately the first use of MediatR notifications
/// in this codebase (see HudhudNestApi.Application.Tests for the Phase 3 finding that no
/// domain-event dispatch pipeline existed anywhere) — chosen because Phase 4, unlike Phase 3, has
/// a genuine need to notify a second bounded context without that context reaching back into
/// Listings' internals.
///
/// Carries only the minimum needed (spec §9: "لا ترسل داخل الحدث بيانات حساسة أو كائن Property
/// كاملاً") — never the Property entity itself.
/// </summary>
public sealed record PropertyPublishedEvent(
    Guid PropertyId,
    DateTime PublishedAtUtc,
    Guid? PublishedByUserId) : INotification;
