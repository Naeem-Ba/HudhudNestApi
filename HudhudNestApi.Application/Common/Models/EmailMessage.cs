namespace HudhudNestApi.Application.Common.Models;

/// <summary>
/// One outgoing message, carrying both renderings of the same content.
///
/// The three-argument <c>SendEmailAsync</c> exists to satisfy ASP.NET Identity's
/// <c>IEmailSender</c>, whose signature is fixed and HTML-only. Providers that
/// accept a plain-text alternative -- Resend among them -- score better on
/// deliverability when they get one, so this record is how callers hand it over
/// without breaking that contract.
/// </summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string Html,
    string? Text = null);
