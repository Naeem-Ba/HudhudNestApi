namespace HudhudNestApi.Domain.Listings.Enums;

/// <summary>
/// Post-share funnel events for a public listing (UTM / Attribution — Phase 2 of the Social
/// Sharing &amp; Distribution feature). Deliberately a separate enum/table from
/// <see cref="Entities.PropertyShareEvent"/> (which only ever records the share action itself):
/// this covers what happens *after* someone opens the shared/attributed link, so a report can
/// answer "did this Facebook share actually lead to a visit, and did that visit contact the
/// owner" — see docs/social-sharing.md §"UTM Attribution" for the full funnel and naming
/// rationale (mirrors the section-15 event names: property_view, property_contact_click, ...).
/// </summary>
public enum PropertyAttributionEventType
{
    /// <summary>The property detail page was opened/rendered — never fired merely because the
    /// Share button was tapped (see PropertyShareEvent for that).</summary>
    View = 1,

    /// <summary>A generic "contact the owner" affordance was used (kept for future contact
    /// entry points that don't fit one of the more specific values below).</summary>
    ContactClick = 2,

    /// <summary>The owner's `tel:` phone link was tapped.</summary>
    PhoneClick = 3,

    /// <summary>A WhatsApp deep link to the owner was tapped.</summary>
    WhatsAppClick = 4,

    /// <summary>An in-app message to the owner was sent successfully (MessagesController).</summary>
    MessageSent = 5,

    /// <summary>
    /// A visit/viewing request was created successfully (VisitsController). This is the closest
    /// equivalent this product has to a "Lead" (property_lead_created in the feature spec) — see
    /// docs/social-sharing.md for why a dedicated Lead entity was not introduced for this phase.
    /// </summary>
    VisitRequestCreated = 6
}
