using System.Globalization;
using System.Text;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Infrastructure.Notifications.Localization;

/// <summary>
/// Every notification sentence in every supported language, in one place. Arabic is the original
/// wording, kept word for word. A missing translation falls back to English, then Arabic, and
/// never produces a raw key in front of a user.
/// Placeholders are {name}; user-written values are inserted verbatim and are never interpreted
/// as placeholders themselves (single pass), and the result is plain text, not markup.
/// </summary>
public static class NotificationCatalog
{
    private sealed record Entry(string Ar, string En, string De);

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
    {
        // ---- placeholders used when a name or title is blank ----
        ["generic.user"] = new("مستخدم", "A user", "Ein Nutzer"),
        ["generic.the_user"] = new("المستخدم", "The user", "Der Nutzer"),
        ["generic.property"] = new("العقار", "the property", "die Immobilie"),
        ["generic.property_noun"] = new("عقار", "property", "Immobilie"),
        ["generic.saved_search"] = new("بحثك المحفوظ", "your saved search", "Ihre gespeicherte Suche"),
        ["generic.agency"] = new("مكتب عقاري", "a real-estate agency", "eine Immobilienagentur"),
        ["generic.agency_member"] = new("عضو المكتب", "an agency member", "ein Agenturmitarbeiter"),

        // ---- outer messages ----
        ["msg.new_message"] = new(
            "أرسل لك {sender} رسالة جديدة.",
            "{sender} sent you a new message.",
            "{sender} hat Ihnen eine neue Nachricht gesendet."),
        ["msg.property_status_changed"] = new(
            "تغيّرت حالة عقارك '{title}': {detail}",
            "The status of your property '{title}' changed: {detail}",
            "Der Status Ihrer Immobilie '{title}' hat sich geändert: {detail}"),
        ["msg.property_price_changed"] = new(
            "تغيّر سعر عقارك '{title}': {detail}",
            "The price of your property '{title}' changed: {detail}",
            "Der Preis Ihrer Immobilie '{title}' hat sich geändert: {detail}"),
        ["msg.property_published"] = new(
            "تم نشر العقار '{title}' بنجاح.",
            "The property '{title}' was published successfully.",
            "Die Immobilie '{title}' wurde erfolgreich veröffentlicht."),
        ["msg.property_unpublished"] = new(
            "تم إلغاء نشر العقار '{title}'.",
            "The property '{title}' was unpublished.",
            "Die Veröffentlichung der Immobilie '{title}' wurde aufgehoben."),
        ["msg.visit_requested"] = new(
            "طلب زيارة جديد لعقارك '{title}': {detail}",
            "New visit request for your property '{title}': {detail}",
            "Neue Besichtigungsanfrage für Ihre Immobilie '{title}': {detail}"),
        ["msg.visit_confirmed"] = new(
            "تم تأكيد موعد زيارتك لعقار '{title}': {detail}",
            "Your visit to '{title}' was confirmed: {detail}",
            "Ihr Besichtigungstermin für '{title}' wurde bestätigt: {detail}"),
        ["msg.visit_declined"] = new(
            "تم رفض طلب زيارتك لعقار '{title}': {detail}",
            "Your visit request for '{title}' was declined: {detail}",
            "Ihre Besichtigungsanfrage für '{title}' wurde abgelehnt: {detail}"),
        ["msg.visit_cancelled"] = new(
            "ألغى الزائر طلب الزيارة لعقارك '{title}': {detail}",
            "The visitor cancelled the visit request for your property '{title}': {detail}",
            "Der Besucher hat die Besichtigungsanfrage für Ihre Immobilie '{title}' storniert: {detail}"),
        ["msg.visit_reschedule_proposed"] = new(
            "اقترح مالك العقار '{title}' موعداً بديلاً لزيارتك: {detail}",
            "The owner of '{title}' proposed another time for your visit: {detail}",
            "Der Eigentümer von '{title}' hat einen anderen Besichtigungstermin vorgeschlagen: {detail}"),
        ["msg.visit_reschedule_accepted"] = new(
            "وافق الزائر على الموعد البديل لزيارة عقارك '{title}': {detail}",
            "The visitor accepted the alternative time for your property '{title}': {detail}",
            "Der Besucher hat den alternativen Termin für Ihre Immobilie '{title}' akzeptiert: {detail}"),
        ["msg.visit_reschedule_declined"] = new(
            "رفض الزائر الموعد البديل لزيارة عقارك '{title}': {detail}",
            "The visitor declined the alternative time for your property '{title}': {detail}",
            "Der Besucher hat den alternativen Termin für Ihre Immobilie '{title}' abgelehnt: {detail}"),
        ["msg.property_update_default"] = new(
            "تحديث على العقار '{title}': {detail}",
            "Update on the property '{title}': {detail}",
            "Aktualisierung zur Immobilie '{title}': {detail}"),
        ["msg.saved_search_match"] = new(
            "عقار جديد '{title}' يطابق '{search}'.",
            "A new property '{title}' matches '{search}'.",
            "Eine neue Immobilie '{title}' passt zu '{search}'."),
        ["msg.user_rated"] = new(
            "قيّمك {rater} بمتوسط {score}/5 على صفحتك الشخصية.",
            "{rater} rated you {score}/5 on average on your profile.",
            "{rater} hat Sie auf Ihrem Profil mit durchschnittlich {score}/5 bewertet."),
        ["msg.agency_member_inquiry"] = new(
            "استفسار جديد: أرسل {customer} رسالة إلى {member} بخصوص '{title}' (مكتب '{agency}').",
            "New inquiry: {customer} sent a message to {member} about '{title}' (agency '{agency}').",
            "Neue Anfrage: {customer} hat {member} eine Nachricht zu '{title}' gesendet (Agentur '{agency}')."),
        ["msg.agency_invitation_received"] = new(
            "دعاك {inviter} للانضمام إلى مكتب '{agency}'. راجع دعواتك لقبولها أو رفضها.",
            "{inviter} invited you to join the agency '{agency}'. Review your invitations to accept or decline.",
            "{inviter} hat Sie eingeladen, der Agentur '{agency}' beizutreten. Prüfen Sie Ihre Einladungen, um sie anzunehmen oder abzulehnen."),
        ["msg.agency_invitation_accepted"] = new(
            "قبل {name} دعوة الانضمام إلى مكتبك.",
            "{name} accepted the invitation to join your agency.",
            "{name} hat die Einladung zu Ihrer Agentur angenommen."),
        ["msg.agency_invitation_declined"] = new(
            "رفض {name} دعوة الانضمام إلى مكتبك.",
            "{name} declined the invitation to join your agency.",
            "{name} hat die Einladung zu Ihrer Agentur abgelehnt."),
        ["msg.valuation_expired_with_estimate"] = new(
            "انتهت مهلة 24 ساعة لطلب التقييم الخاص بك. كان لديك تقدير مبدئي مبني على إعلانات مشابهة، لكن لم يكتمل التقييم النهائي ضمن المهلة المحددة.",
            "The 24-hour window for your valuation request has ended. You had a preliminary estimate based on similar listings, but the final valuation was not completed in time.",
            "Die 24-Stunden-Frist für Ihre Bewertungsanfrage ist abgelaufen. Sie hatten eine vorläufige Schätzung auf Basis ähnlicher Anzeigen, die endgültige Bewertung wurde jedoch nicht fristgerecht abgeschlossen."),
        ["msg.valuation_expired_no_estimate"] = new(
            "انتهت مهلة 24 ساعة لطلب التقييم الخاص بك دون التوصل إلى تقدير. يمكنك تقديم طلب تقييم جديد.",
            "The 24-hour window for your valuation request has ended without an estimate. You can submit a new valuation request.",
            "Die 24-Stunden-Frist für Ihre Bewertungsanfrage ist ohne Schätzung abgelaufen. Sie können eine neue Bewertungsanfrage stellen."),
        ["msg.valuation_office_invitation_expired"] = new(
            "انتهت صلاحية دعوة تقييم عقاري دون تقديم رد من مكتبك ضمن المهلة المحددة.",
            "A property valuation invitation expired without a response from your agency within the deadline.",
            "Eine Einladung zur Immobilienbewertung ist abgelaufen, ohne dass Ihre Agentur innerhalb der Frist geantwortet hat."),
        ["msg.valuation_result_ready"] = new(
            "اكتمل تقييم عقارك: ردّت جميع المكاتب المدعوة على طلبك. يمكنك الاطلاع على النتيجة الآن.",
            "Your property valuation is complete: all invited agencies have responded. You can view the result now.",
            "Die Bewertung Ihrer Immobilie ist abgeschlossen: Alle eingeladenen Agenturen haben geantwortet. Sie können das Ergebnis jetzt ansehen."),
        ["msg.valuation_reminder_soon"] = new(
            "لم يكتمل تقييم عقارك بعد، وستنتهي المهلة المحددة (24 ساعة) خلال ساعات قليلة.",
            "Your property valuation is not complete yet, and the 24-hour window ends in a few hours.",
            "Die Bewertung Ihrer Immobilie ist noch nicht abgeschlossen, und die 24-Stunden-Frist endet in wenigen Stunden."),
        ["msg.listing_expiring_today"] = new(
            "ينتهي نشر إعلانك '{title}' اليوم. مدِّده للإبقاء عليه ظاهراً.",
            "Your listing '{title}' stops being published today. Extend it to keep it visible.",
            "Die Veröffentlichung Ihrer Anzeige '{title}' endet heute. Verlängern Sie sie, damit sie sichtbar bleibt."),
        ["msg.listing_expiring_days"] = new(
            "ينتهي نشر إعلانك '{title}' خلال {days} يوماً. مدِّده للإبقاء عليه ظاهراً.",
            "Your listing '{title}' stops being published in {days} days. Extend it to keep it visible.",
            "Die Veröffentlichung Ihrer Anzeige '{title}' endet in {days} Tagen. Verlängern Sie sie, damit sie sichtbar bleibt."),
        ["msg.listing_taken_down"] = new(
            "أوقفت الإدارة إعلانك '{title}' ولم يعد ظاهراً للعموم. السبب: {detail}",
            "An administrator took your listing '{title}' down and it is no longer public. Reason: {detail}",
            "Ein Administrator hat Ihre Anzeige '{title}' offline genommen, sie ist nicht mehr öffentlich. Grund: {detail}"),
        ["msg.listing_restored"] = new(
            "أعادت الإدارة نشر إعلانك '{title}' وهو ظاهر للعموم من جديد.",
            "An administrator restored your listing '{title}' and it is public again.",
            "Ein Administrator hat Ihre Anzeige '{title}' wiederhergestellt, sie ist wieder öffentlich."),
        ["msg.visit_cancelled_by_host"] = new(
            "أُلغي طلب زيارتك للعقار '{title}' لأن الإعلان لم يعد متاحاً. {detail}",
            "Your visit request for the property '{title}' was cancelled because the listing is no longer available. {detail}",
            "Ihre Besichtigungsanfrage für die Immobilie '{title}' wurde storniert, da die Anzeige nicht mehr verfügbar ist. {detail}"),
        ["msg.listing_expired"] = new(
            "انتهت مدة نشر إعلانك '{title}' ولم يعد ظاهراً في نتائج البحث. أمامك {days} يوماً لتمديده قبل حذفه.",
            "Your listing '{title}' is no longer published and no longer appears in search results. You have {days} days to extend it before it is deleted.",
            "Die Veröffentlichung Ihrer Anzeige '{title}' ist abgelaufen, sie erscheint nicht mehr in den Suchergebnissen. Sie haben {days} Tage, sie zu verlängern, bevor sie gelöscht wird."),

        // ---- details: property / visits ----
        ["detail.status_change"] = new("{from} → {to}", "{from} → {to}", "{from} → {to}"),
        ["detail.cold_rent"] = new(
            "الإيجار البارد: {from} → {to}", "Cold rent: {from} → {to}", "Kaltmiete: {from} → {to}"),
        ["detail.warm_rent"] = new(
            "الإيجار الشامل: {from} → {to}", "Warm rent: {from} → {to}", "Warmmiete: {from} → {to}"),
        ["detail.purchase_price"] = new(
            "سعر الشراء: {from} → {to}", "Purchase price: {from} → {to}", "Kaufpreis: {from} → {to}"),
        ["detail.review_added"] = new(
            "تم إضافة تقييم جديد ({rating}/5) لعقارك.",
            "A new review ({rating}/5) was added to your property.",
            "Ihre Immobilie hat eine neue Bewertung ({rating}/5) erhalten."),
        ["note.owner"] = new(" — ملاحظة المالك: {note}", " — Owner's note: {note}", " — Hinweis des Eigentümers: {note}"),
        ["note.decline_reason"] = new(" — سبب الرفض: {note}", " — Reason: {note}", " — Grund: {note}"),
        ["note.visitor"] = new(" — ملاحظة الزائر: {note}", " — Visitor's note: {note}", " — Hinweis des Besuchers: {note}"),
        ["visit.detail.requested"] = new(
            "{name} بتاريخ {at}.{note}", "{name} on {at}.{note}", "{name} am {at}.{note}"),
        ["visit.detail.confirmed"] = new(
            "موعد الزيارة بتاريخ {at}.{note}", "Visit on {at}.{note}", "Besichtigung am {at}.{note}"),
        ["visit.detail.declined"] = new(
            "طلب الزيارة بتاريخ {at}.{note}", "Visit request for {at}.{note}", "Besichtigungsanfrage für den {at}.{note}"),
        ["visit.detail.cancelled"] = new(
            "{name} — الموعد الملغى: {at}.", "{name} — cancelled time: {at}.", "{name} — stornierter Termin: {at}."),
        ["visit.detail.cancelled_listing_removed"] = new(
            "الموعد الملغى: {at}.", "Cancelled time: {at}.", "Stornierter Termin: {at}."),
        ["visit.detail.proposed"] = new(
            "الموعد الجديد المقترح: {at}.{note}", "Proposed new time: {at}.{note}", "Vorgeschlagener neuer Termin: {at}.{note}"),
        ["visit.detail.reschedule_accepted"] = new(
            "{name} — الموعد المؤكَّد: {at}.", "{name} — confirmed time: {at}.", "{name} — bestätigter Termin: {at}."),
        ["visit.detail.reschedule_declined"] = new(
            "{name} — الموعد المرفوض: {at}.", "{name} — declined time: {at}.", "{name} — abgelehnter Termin: {at}."),

        // ---- details: service marketplace ----
        ["service.detail.submitted"] = new(
            "طلب خدمة جديد ({number}) على عقار '{title}'.",
            "New service request ({number}) for the property '{title}'.",
            "Neue Serviceanfrage ({number}) für die Immobilie '{title}'."),
        ["service.detail.accepted"] = new(
            "تم قبول طلبك ({number}) من قِبل {provider}.",
            "Your request ({number}) was accepted by {provider}.",
            "Ihre Anfrage ({number}) wurde von {provider} angenommen."),
        ["service.detail.rejected"] = new(
            "تم رفض طلبك ({number}): {reason}",
            "Your request ({number}) was rejected: {reason}",
            "Ihre Anfrage ({number}) wurde abgelehnt: {reason}"),
        ["service.detail.scheduled"] = new(
            "تمت جدولة طلبك ({number}) بتاريخ {at}.",
            "Your request ({number}) was scheduled for {at}.",
            "Ihre Anfrage ({number}) wurde für den {at} terminiert."),
        ["service.detail.completed"] = new(
            "أعلن المزوّد إتمام طلبك ({number}). أكّد لنا إن كانت الخدمة قد تمّت فعلًا، ثم قيّمها.",
            "The provider marked your request ({number}) as completed. Please confirm it was done, then rate the service.",
            "Der Anbieter hat Ihre Anfrage ({number}) als erledigt gemeldet. Bitte bestätigen Sie das und bewerten Sie den Service."),
        ["service.detail.not_completed"] = new(
            "أعلن المزوّد أن طلبك ({number}) لم يتم. أخبرنا بسببك من جهتك ليساعدنا ذلك على تحسين الخدمات.",
            "The provider marked your request ({number}) as not completed. Please tell us why from your side; it helps us improve the services.",
            "Der Anbieter hat Ihre Anfrage ({number}) als nicht erledigt gemeldet. Bitte nennen Sie uns Ihren Grund; das hilft uns, die Services zu verbessern."),
        ["service.detail.inquiry"] = new(
            "استفسار جديد ({number}) عن خدمتك على عقار '{title}'. ردّ عليه من صفحة الطلبات.",
            "New inquiry ({number}) about your service for the property '{title}'. Answer it from the requests page.",
            "Neue Anfrage ({number}) zu Ihrem Service für die Immobilie '{title}'. Antworten Sie über die Anfragenseite."),
        ["service.detail.message"] = new(
            "رسالة جديدة في الطلب ({number}).",
            "New message in the request ({number}).",
            "Neue Nachricht in der Anfrage ({number})."),
        ["service.detail.confirm_reminder"] = new(
            "لم تؤكد طلبك ({number}) بعد. إن أصبحت جاهزًا افتح المحادثة واضغط «تأكيد الطلب»، وإلا فلا يلزمك شيء.",
            "You have not confirmed your request ({number}) yet. When you are ready, open the conversation and press \"Confirm request\"; otherwise you do not need to do anything.",
            "Sie haben Ihre Anfrage ({number}) noch nicht bestätigt. Öffnen Sie die Unterhaltung und tippen Sie auf \"Anfrage bestätigen\", wenn Sie bereit sind; sonst müssen Sie nichts tun."),
        ["service.detail.expired"] = new(
            "أُغلق الطلب ({number}) تلقائيًا لعدم وجود أي تواصل منذ شهر. المحادثة محفوظة ويمكنك البدء من جديد.",
            "The request ({number}) was closed automatically after a month without contact. The conversation is kept and you can start again.",
            "Die Anfrage ({number}) wurde nach einem Monat ohne Kontakt automatisch geschlossen. Die Unterhaltung bleibt erhalten, Sie können neu beginnen."),
        ["service.detail.cancelled"] = new(
            "تم إلغاء طلب الخدمة ({number}).",
            "The service request ({number}) was cancelled.",
            "Die Serviceanfrage ({number}) wurde storniert."),
        ["service.detail.review_added"] = new(
            "تم إضافة تقييم جديد ({rating}/5) لطلب الخدمة ({number}).",
            "A new review ({rating}/5) was added for the service request ({number}).",
            "Für die Serviceanfrage ({number}) wurde eine neue Bewertung ({rating}/5) abgegeben."),

        // ---- service provider onboarding and management ----
        ["msg.provider_application_info_requested"] = new(
            "طلب انضمامك كمزوّد خدمة ({ref}) يحتاج إلى معلومات إضافية: {message}",
            "Your service provider application ({ref}) needs more information: {message}",
            "Ihre Bewerbung als Dienstleister ({ref}) benötigt weitere Angaben: {message}"),
        ["msg.provider_application_approved"] = new(
            "تم اعتمادك مزوّد خدمة في هدهد نيست (الطلب {ref}). يمكنك الآن إضافة خدماتك من لوحة المزوّد.",
            "You are now an approved HudhudNest service provider (application {ref}). You can add your services from the provider dashboard.",
            "Sie sind jetzt ein zugelassener HudhudNest-Dienstleister (Bewerbung {ref}). Sie können Ihre Leistungen im Anbieter-Bereich anlegen."),
        ["msg.provider_application_rejected"] = new(
            "تم رفض طلب انضمامك كمزوّد خدمة ({ref}). السبب: {reason}",
            "Your service provider application ({ref}) was rejected. Reason: {reason}",
            "Ihre Bewerbung als Dienstleister ({ref}) wurde abgelehnt. Grund: {reason}"),
        ["msg.provider_suspended"] = new(
            "تم إيقاف ملف مزوّد الخدمة '{name}' مؤقتًا، ولن يظهر للعملاء أو يستقبل طلبات جديدة. السبب: {reason}",
            "Your service provider profile '{name}' was suspended and will not be shown or receive new requests. Reason: {reason}",
            "Ihr Dienstleisterprofil '{name}' wurde ausgesetzt und wird nicht angezeigt und erhält keine neuen Anfragen. Grund: {reason}"),
        ["msg.provider_reactivated"] = new(
            "أُعيد تفعيل ملف مزوّد الخدمة '{name}'، وأصبحت خدماتك المعتمدة ظاهرة للعملاء مجددًا.",
            "Your service provider profile '{name}' is active again and your approved services are visible to customers.",
            "Ihr Dienstleisterprofil '{name}' ist wieder aktiv und Ihre zugelassenen Leistungen sind wieder sichtbar."),
        ["msg.provider_revoked"] = new(
            "تم إلغاء اعتماد '{name}' كمزوّد خدمة في هدهد نيست. السبب: {reason}",
            "The approval of '{name}' as a HudhudNest service provider was revoked. Reason: {reason}",
            "Die Zulassung von '{name}' als HudhudNest-Dienstleister wurde widerrufen. Grund: {reason}"),

        // ---- details: short stay ----
        ["shortstay.detail.requested"] = new(
            "طلب حجز جديد لـ '{title}' من {from} إلى {to}.",
            "New booking request for '{title}' from {from} to {to}.",
            "Neue Buchungsanfrage für '{title}' vom {from} bis {to}."),
        ["shortstay.detail.approved"] = new(
            "تمت الموافقة على حجزك لـ '{title}'.",
            "Your booking for '{title}' was approved.",
            "Ihre Buchung für '{title}' wurde genehmigt."),
        ["shortstay.detail.other_booking_taken"] = new(
            "للأسف تم قبول حجز آخر لنفس التواريخ في '{title}'.",
            "Unfortunately, another booking for the same dates at '{title}' was accepted.",
            "Leider wurde für '{title}' eine andere Buchung für dieselben Termine angenommen."),
        ["shortstay.detail.confirmed"] = new(
            "تم تأكيد حجزك لـ '{title}'.",
            "Your booking for '{title}' was confirmed.",
            "Ihre Buchung für '{title}' wurde bestätigt."),
        ["shortstay.detail.rejected"] = new(
            "تم رفض حجزك لـ '{title}'.",
            "Your booking for '{title}' was rejected.",
            "Ihre Buchung für '{title}' wurde abgelehnt."),
        ["shortstay.detail.cancelled_by_guest"] = new(
            "ألغى الضيف حجزه لـ '{title}'.",
            "The guest cancelled their booking for '{title}'.",
            "Der Gast hat seine Buchung für '{title}' storniert."),
        ["shortstay.detail.review_added"] = new(
            "حصل إعلانك '{title}' على تقييم جديد.",
            "Your listing '{title}' received a new review.",
            "Ihre Anzeige '{title}' hat eine neue Bewertung erhalten."),
    };

    /// <summary>Every key, for tests that prove all three languages are filled in.</summary>
    public static IReadOnlyCollection<string> Keys => Entries.Keys;

    public static bool TryGetTemplate(string key, string language, out string template)
    {
        template = string.Empty;
        if (!Entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        var candidate = language switch
        {
            SupportedPreferences.German => entry.De,
            SupportedPreferences.English => entry.En,
            _ => entry.Ar,
        };

        // A blank translation is a gap, not a translation: fall back rather than show nothing.
        template = !string.IsNullOrWhiteSpace(candidate)
            ? candidate
            : !string.IsNullOrWhiteSpace(entry.En) ? entry.En : entry.Ar;
        return true;
    }

    public static string Render(NotificationText text, string language)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Key is null)
        {
            return text.Literal ?? string.Empty;
        }

        if (!TryGetTemplate(text.Key, language, out var template))
        {
            // An unknown key is a programming error; show nothing technical to the user.
            return text.Literal ?? string.Empty;
        }

        return Fill(template, text.Args, language);
    }

    /// <summary>Single left-to-right pass: values are copied, never re-scanned for placeholders.</summary>
    private static string Fill(string template, IReadOnlyDictionary<string, object?> args, string language)
    {
        var sb = new StringBuilder(template.Length + 32);
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{')
            {
                var close = template.IndexOf('}', i + 1);
                if (close > i)
                {
                    var name = template.Substring(i + 1, close - i - 1);
                    if (args.TryGetValue(name, out var value))
                    {
                        sb.Append(Format(value, language));
                        i = close + 1;
                        continue;
                    }
                }
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static string Format(object? value, string language) => value switch
    {
        null => string.Empty,
        NotificationText nested => Render(nested, language),
        string s => s,
        DateTime dt => dt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        double d => d.ToString("0.0", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
