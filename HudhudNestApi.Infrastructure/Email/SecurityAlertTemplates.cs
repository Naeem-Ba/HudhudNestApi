using System.Globalization;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Infrastructure.Email;

/// <summary>
/// Subject and plain-text body of every security alert, in the account owner's language.
/// English is the original wording, kept as it was (it is also what an account that never chose a
/// language receives, i.e. exactly what it received before). Links are filled in by the callers that
/// have them; the [PLACEHOLDER] link lines are unchanged from before localization.
/// </summary>
public static class SecurityAlertTemplates
{
    private const string Rule = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
    private const string ShortRule = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";

    /// <summary>The language a security email uses when the account has not chosen one.</summary>
    public const string LegacyLanguage = SupportedPreferences.English;

    public static string Resolve(string? language) =>
        SupportedPreferences.NormalizeLanguage(language) ?? LegacyLanguage;

    private static string Stamp(DateTime utc, string lang) =>
        lang == SupportedPreferences.English
            ? utc.ToString("F", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Unknown(string lang) => lang switch
    {
        SupportedPreferences.Arabic => "غير معروف",
        SupportedPreferences.German => "Unbekannt",
        _ => "Unknown",
    };

    // ------------------------------------------------------------------ failed login

    public static (string Subject, string Body) FailedLogin(
        string lang, string userName, int failedAttemptCount, string? ipAddress, DateTime attemptTimeUtc)
    {
        lang = Resolve(lang);
        var ip = ipAddress ?? Unknown(lang);
        var at = Stamp(attemptTimeUtc, lang);

        return lang switch
        {
            SupportedPreferences.Arabic => (
                failedAttemptCount switch
                {
                    3 => "⚠️ تنبيه أمني: 3 محاولات دخول فاشلة إلى حسابك",
                    4 => "🔴 هام: 4 محاولات دخول فاشلة - سيُقفل حسابك قريبًا",
                    _ => "محاولة دخول فاشلة إلى حسابك",
                },
                $@"
مرحبًا {userName}،

{failedAttemptCount switch
                {
                    3 => "⚠️ تحذير: رُصدت ثلاث محاولات دخول فاشلة\nإن لم تكن أنت، فحسابك ما زال متاحًا. يُرجى تغيير كلمة المرور فورًا.",
                    4 => "🔴 هام: رُصدت أربع محاولات دخول فاشلة\nسيُقفل حسابك بعد محاولة فاشلة واحدة أخرى!\nإن لم تكن أنت، غيّر كلمة المرور الآن لحماية حسابك.",
                    _ => "ℹ️ لم تنجح هذه المحاولة.\nإن لم تكن أنت، فكّر في تغيير كلمة المرور.",
                }}

تفاصيل محاولة الدخول الفاشلة:
{ShortRule}
المحاولة رقم {failedAttemptCount} في {at} (UTC)
من عنوان IP: {ip}
{ShortRule}

{Rule}
✅ إن كنت تعرف هذه المحاولة: تجاهل هذه الرسالة. لا يلزمك أي إجراء.
{Rule}

❌ إن كنت لا تعرفها:
{(failedAttemptCount >= 3 ? "⏰ غيّر كلمة المرور الآن قبل أن يُقفل حسابك!" : "")}

🔗 لتأمين حسابك:
• تغيير كلمة المرور: [CHANGE_PASSWORD_LINK]
• مراجعة الأمان: [SECURITY_DASHBOARD_LINK]
• إلغاء الوصول: [REVOKE_SESSIONS_LINK]

{Rule}
إن احتجت مساعدة فتواصل مع فريق الدعم فورًا.
هذا تنبيه أمني آلي - لا ترد على هذه الرسالة.
"),

            SupportedPreferences.German => (
                failedAttemptCount switch
                {
                    3 => "⚠️ Sicherheitshinweis: 3 fehlgeschlagene Anmeldeversuche bei Ihrem Konto",
                    4 => "🔴 Wichtig: 4 fehlgeschlagene Anmeldeversuche - Ihr Konto wird bald gesperrt",
                    _ => "Fehlgeschlagener Anmeldeversuch bei Ihrem Konto",
                },
                $@"
Hallo {userName},

{failedAttemptCount switch
                {
                    3 => "⚠️ WARNUNG: Drei fehlgeschlagene Versuche erkannt\nWenn Sie das nicht waren, ist Ihr Konto weiterhin zugänglich. Bitte ändern Sie sofort Ihr Passwort.",
                    4 => "🔴 WICHTIG: Vier fehlgeschlagene Versuche erkannt\nIhr Konto wird nach einem weiteren fehlgeschlagenen Versuch gesperrt!\nWenn Sie das nicht waren, ändern Sie JETZT Ihr Passwort, um Ihr Konto zu schützen.",
                    _ => "ℹ️ Dieser Versuch war nicht erfolgreich.\nWenn Sie das nicht waren, sollten Sie Ihr Passwort ändern.",
                }}

Details zum fehlgeschlagenen Anmeldeversuch:
{ShortRule}
Versuch Nr. {failedAttemptCount} am {at} (UTC)
Von IP-Adresse: {ip}
{ShortRule}

{Rule}
✅ WENN SIE DAS WAREN: Ignorieren Sie diese E-Mail. Sie müssen nichts tun.
{Rule}

❌ WENN SIE DAS NICHT WAREN:
{(failedAttemptCount >= 3 ? "⏰ Ändern Sie JETZT Ihr Passwort, bevor Ihr Konto gesperrt wird!" : "")}

🔗 Konto absichern:
• Passwort ändern: [CHANGE_PASSWORD_LINK]
• Sicherheit prüfen: [SECURITY_DASHBOARD_LINK]
• Zugriff sperren: [REVOKE_SESSIONS_LINK]

{Rule}
Wenn Sie Hilfe benötigen, wenden Sie sich sofort an unser Support-Team.
Dies ist eine automatische Sicherheitsmeldung - bitte nicht antworten.
"),

            _ => (
                failedAttemptCount switch
                {
                    3 => "⚠️ Security Alert: 3 Failed Login Attempts to Your Account",
                    4 => "🔴 Critical: 4 Failed Login Attempts - Your Account Will Lock Soon",
                    _ => "Failed Login Attempt to Your Account",
                },
                $@"
Hello {userName},

{failedAttemptCount switch
                {
                    3 => "\n⚠️ WARNING: Three failed attempts detected\nIf this wasn't you, your account is still accessible. Please change your password immediately.\n",
                    4 => "\n🔴 CRITICAL: Four failed attempts detected\nYour account will be locked after one more failed attempt!\nIf this wasn't you, change your password RIGHT NOW to secure your account.\n",
                    _ => "\nℹ️ This attempt was unsuccessful.\nIf this wasn't you, consider changing your password.\n",
                }}

Failed Login Attempt Details:
{ShortRule}
Attempt #{failedAttemptCount} at {at} (UTC)
From IP Address: {ip}
{ShortRule}

{Rule}
✅ IF YOU RECOGNIZE THIS: Ignore this email. No action needed.
{Rule}

❌ IF YOU DON'T RECOGNIZE THIS:
{(failedAttemptCount >= 3 ? "⏰ Change your password NOW before your account is locked!" : "")}

🔗 Secure Your Account:
• Change Password: [CHANGE_PASSWORD_LINK]
• Review Security: [SECURITY_DASHBOARD_LINK]
• Disable Access: [REVOKE_SESSIONS_LINK]

{Rule}
If you need help, contact our support team immediately.
This is an automated security alert - do not reply to this email.
"),
        };
    }

    // ------------------------------------------------------------------ password change request

    public static (string Subject, string Body) PasswordChangeRequest(
        string lang, string userName, string passwordResetLink, int failedAttemptCount, string? ipAddress)
    {
        lang = Resolve(lang);
        var ip = ipAddress ?? Unknown(lang);
        var urgent = failedAttemptCount >= 4;
        var locked = failedAttemptCount >= 5;

        return lang switch
        {
            SupportedPreferences.Arabic => (
                urgent
                    ? "🔴 عاجل: غيّر كلمة المرور فورًا - احتمال وصول غير مصرّح به"
                    : "🔒 إجراء أمني مطلوب: حدّث كلمة المرور",
                $@"
مرحبًا {userName}،

رُصدت محاولة وصول غير مصرّح بها محتملة
{Rule}

تفاصيل المحاولات:
• عدد المحاولات الفاشلة: {failedAttemptCount}
• آخر محاولة من: {ip}
• حالة الحساب: {(locked ? "🔴 مقفل (15 دقيقة)" : "⚠️ معرّض للخطر")}

إجراء فوري مطلوب:
{Rule}

👉 اضغط الرابط أدناه لتغيير كلمة المرور:
{passwordResetLink}

ينتهي الرابط خلال: 24 ساعة

⚠️ إن كنت تعرف هذه المحاولات:
ربما:
• شاركت كلمة المرور عن غير قصد
• استخدمت كلمة المرور نفسها في موقع آخر تعرّض للاختراق
• تعرّضت لهجمات آلية

خطة العمل:
1. ✅ غيّر كلمة المرور إلى كلمة قوية وفريدة
2. ✅ لا تشارك كلمة المرور مع أحد
3. ✅ فعّل المصادقة الثنائية (إن توفرت)
4. ✅ راجع الجلسات النشطة وألغِ المشبوه منها

{Rule}

نصائح أمنية:
• استخدم مدير كلمات مرور لتوليد كلمات قوية
• فعّل المصادقة الثنائية لحماية إضافية
• راجع نشاط الدخول بانتظام

أسئلة؟ تواصل مع فريق الأمان: security@hudhudnest.com

هذا تنبيه أمني آلي - لا ترد على هذه الرسالة.
"),

            SupportedPreferences.German => (
                urgent
                    ? "🔴 DRINGEND: Ändern Sie sofort Ihr Passwort - möglicher unbefugter Zugriff"
                    : "🔒 Sicherheitsmaßnahme erforderlich: Passwort aktualisieren",
                $@"
Hallo {userName},

MÖGLICHER UNBEFUGTER ZUGRIFFSVERSUCH ERKANNT
{Rule}

Details:
• Fehlgeschlagene Versuche: {failedAttemptCount}
• Letzter Versuch von: {ip}
• Kontostatus: {(locked ? "🔴 GESPERRT (15 Minuten)" : "⚠️ GEFÄHRDET")}

SOFORTIGES HANDELN ERFORDERLICH:
{Rule}

👉 Klicken Sie auf den folgenden Link, um Ihr Passwort zu ÄNDERN:
{passwordResetLink}

Der Link läuft ab in: 24 Stunden

⚠️ WENN SIE DIESE VERSUCHE KENNEN:
Möglicherweise haben Sie:
• Ihr Passwort versehentlich weitergegeben
• dasselbe Passwort auf einer anderen, gehackten Seite verwendet
• automatisierte Angriffe erlebt

VORGEHEN:
1. ✅ Ändern Sie Ihr Passwort in ein starkes, einzigartiges Passwort
2. ✅ Geben Sie Ihr Passwort niemals weiter
3. ✅ Aktivieren Sie die Zwei-Faktor-Authentifizierung (falls verfügbar)
4. ✅ Prüfen Sie aktive Sitzungen und beenden Sie verdächtige

{Rule}

SICHERHEITSTIPPS:
• Nutzen Sie einen Passwortmanager für starke Passwörter
• Aktivieren Sie 2FA für zusätzlichen Schutz
• Prüfen Sie regelmäßig die Anmeldeaktivität

Fragen? Kontaktieren Sie unser Sicherheitsteam: security@hudhudnest.com

Dies ist eine automatische Sicherheitsmeldung - bitte nicht antworten.
"),

            _ => (
                urgent
                    ? "🔴 URGENT: Change Your Password Immediately - Potential Unauthorized Access"
                    : "🔒 Security Action Required: Update Your Password",
                $@"
Hello {userName},

POTENTIAL UNAUTHORIZED ACCESS ATTEMPT DETECTED
{Rule}

Attack Details:
• Failed Attempts: {failedAttemptCount} attempts
• Last Attempt From: {ip}
• Account Status: {(locked ? "🔴 LOCKED (15 minutes)" : "⚠️ AT RISK")}

IMMEDIATE ACTION REQUIRED:
{Rule}

👉 Click the link below to CHANGE YOUR PASSWORD:
{passwordResetLink}

Link expires in: 24 hours

⚠️ IF YOU RECOGNIZE THESE ATTEMPTS:
You may have:
• Shared your password accidentally
• Reused password on another site that was breached
• Been targeted by automated attacks

ACTION PLAN:
1. ✅ Change your password to a strong, unique password
2. ✅ Never share your password with anyone
3. ✅ Enable two-factor authentication (if available)
4. ✅ Check active sessions and revoke suspicious ones

{Rule}

SECURITY TIPS:
• Use a password manager to generate strong passwords
• Enable 2FA for additional protection
• Review login activity regularly

Questions? Contact our security team: security@hudhudnest.com

This is an automated security alert - do not reply to this email.
"),
        };
    }

    // ------------------------------------------------------------------ account locked

    public static (string Subject, string Body) AccountLocked(
        string lang, string userName, DateTime? lockoutEnd, string? ipAddress, int minutesRemaining)
    {
        lang = Resolve(lang);
        var ip = ipAddress ?? Unknown(lang);
        var unlockAt = lockoutEnd.HasValue ? Stamp(lockoutEnd.Value, lang) : string.Empty;

        return lang switch
        {
            SupportedPreferences.Arabic => (
                "🔴 هام: تم قفل حسابك",
                $@"
مرحبًا {userName}،

حسابك مقفل
{Rule}

⛔ رُصدت محاولات دخول فاشلة كثيرة

تفاصيل القفل:
• السبب: 5 محاولات دخول فاشلة أو أكثر
• القفل من: {ip}
• المدة: {minutesRemaining} دقيقة
• وقت فك القفل: {(lockoutEnd.HasValue ? unlockAt + " UTC" : "تلقائي")}

{Rule}
ما العمل؟
{Rule}

✅ انتظر: سيُفك قفل حسابك تلقائيًا بعد {minutesRemaining} دقيقة
   ثم حاول الدخول بكلمة المرور الصحيحة.

❌ لا تعرف هذه المحاولات؟
   إجراءات فورية:
   1. 🔑 غيّر كلمة المرور: [PASSWORD_RESET_LINK]
   2. 🛡️ أمّن حسابك: [SECURITY_SETTINGS_LINK]
   3. 📊 راجع سجل الدخول: [LOGIN_HISTORY_LINK]

⏰ يُفك القفل في: {unlockAt} UTC
   (بعد {minutesRemaining} دقيقة من الآن)

{Rule}

قائمة التحقق الأمني:
□ غيّر كلمة المرور إلى كلمة قوية وفريدة
□ تأكد من عدم وجود نشاط مشبوه في حسابك
□ افحص جهازك بحثًا عن برمجيات خبيثة
□ راجع خيارات الاسترداد (البريد، الهاتف) وحدّثها
□ فعّل المصادقة الثنائية

ما زال الحساب مقفلًا بعد {minutesRemaining} دقيقة؟
تواصل مع الدعم: support@hudhudnest.com

هذا تنبيه أمني آلي - لا ترد على هذه الرسالة.
"),

            SupportedPreferences.German => (
                "🔴 WICHTIG: Ihr Konto wurde gesperrt",
                $@"
Hallo {userName},

IHR KONTO IST GESPERRT
{Rule}

⛔ Zu viele fehlgeschlagene Anmeldeversuche erkannt

Details zur Sperre:
• Grund: 5 oder mehr fehlgeschlagene Anmeldeversuche
• Gesperrt von: {ip}
• Dauer: {minutesRemaining} Minuten
• Entsperrung: {(lockoutEnd.HasValue ? unlockAt + " UTC" : "Automatisch")}

{Rule}
WAS SIE TUN KÖNNEN:
{Rule}

✅ WARTEN: Ihr Konto wird in {minutesRemaining} Minuten automatisch entsperrt
   Melden Sie sich danach mit Ihrem richtigen Passwort erneut an.

❌ SIE KENNEN DIESE VERSUCHE NICHT?
   Sofortmaßnahmen:
   1. 🔑 Passwort ändern: [PASSWORD_RESET_LINK]
   2. 🛡️ Konto absichern: [SECURITY_SETTINGS_LINK]
   3. 📊 Anmeldeverlauf prüfen: [LOGIN_HISTORY_LINK]

⏰ KONTO WIRD ENTSPERRT UM: {unlockAt} UTC
   (in {minutesRemaining} Minuten)

{Rule}

SICHERHEITS-CHECKLISTE:
□ Ändern Sie Ihr Passwort in ein starkes, einzigartiges Passwort
□ Prüfen Sie Ihr Konto auf verdächtige Aktivitäten
□ Prüfen Sie Ihren Computer auf Schadsoftware
□ Prüfen und aktualisieren Sie die Wiederherstellungsoptionen (E-Mail, Telefon)
□ Aktivieren Sie die Zwei-Faktor-Authentifizierung

NACH {minutesRemaining} MINUTEN NOCH GESPERRT?
Support kontaktieren: support@hudhudnest.com

Dies ist eine automatische Sicherheitsmeldung - bitte nicht antworten.
"),

            _ => (
                "🔴 CRITICAL: Your Account Has Been Locked",
                $@"
Hello {userName},

YOUR ACCOUNT IS LOCKED
{Rule}

⛔ Too many failed login attempts detected

Lock Details:
• Reason: 5+ failed login attempts
• Locked From: {ip}
• Duration: {minutesRemaining} minutes
• Unlock Time: {(lockoutEnd.HasValue ? unlockAt + " UTC" : "Automatic")}

{Rule}
WHAT TO DO:
{Rule}

✅ WAIT: Your account will automatically unlock in {minutesRemaining} minutes
   Then try logging in again with your correct password.

❌ DON'T RECOGNIZE THESE ATTEMPTS?
   Immediate actions:
   1. 🔑 Change your password: [PASSWORD_RESET_LINK]
   2. 🛡️ Secure your account: [SECURITY_SETTINGS_LINK]
   3. 📊 Review login history: [LOGIN_HISTORY_LINK]

⏰ ACCOUNT WILL UNLOCK AT: {unlockAt} UTC
   ({minutesRemaining} minutes from now)

{Rule}

SECURITY CHECKLIST:
□ Change your password to something strong and unique
□ Verify no suspicious activity on your account
□ Check your computer for malware
□ Review and update recovery options (email, phone)
□ Enable two-factor authentication

STILL LOCKED AFTER {minutesRemaining} MINUTES?
Contact support: support@hudhudnest.com

This is an automated security alert - do not reply to this email.
"),
        };
    }

    // ------------------------------------------------------------------ password changed

    public static (string Subject, string Body) PasswordChanged(string lang, string userName, DateTime nowUtc)
    {
        lang = Resolve(lang);
        var at = Stamp(nowUtc, lang);

        return lang switch
        {
            SupportedPreferences.Arabic => (
                "✅ تم تغيير كلمة المرور بنجاح",
                $@"
مرحبًا {userName}،

تم تغيير كلمة مرورك بنجاح
{Rule}

✅ تم تحديث كلمة مرور حسابك بنجاح في {at} UTC

حسابك الآن محمي بكلمة المرور الجديدة.

{Rule}
الخطوات التالية:
{Rule}

1. ✅ سجّل الدخول بكلمة المرور الجديدة
2. ✅ راجع جلساتك النشطة
3. ✅ تأكد من أن جميع مواقع الدخول مصرّح بها

توصية أمنية:
فكّر في تفعيل المصادقة الثنائية لحماية إضافية لحسابك.

{Rule}

إن لم تغيّر كلمة المرور بنفسك:
قد يكون حسابك مخترقًا! تواصل مع الدعم فورًا:
support@hudhudnest.com

هذا إشعار آلي - لا ترد على هذه الرسالة.
"),

            SupportedPreferences.German => (
                "✅ Passwort erfolgreich geändert",
                $@"
Hallo {userName},

IHR PASSWORT WURDE ERFOLGREICH GEÄNDERT
{Rule}

✅ Das Passwort Ihres Kontos wurde am {at} UTC erfolgreich aktualisiert

Ihr Konto ist nun mit dem neuen Passwort geschützt.

{Rule}
NÄCHSTE SCHRITTE:
{Rule}

1. ✅ Melden Sie sich mit Ihrem neuen Passwort an
2. ✅ Prüfen Sie Ihre aktiven Sitzungen
3. ✅ Stellen Sie sicher, dass alle Anmeldeorte autorisiert sind

SICHERHEITSEMPFEHLUNG:
Erwägen Sie die Zwei-Faktor-Authentifizierung für zusätzlichen Kontoschutz.

{Rule}

WENN SIE IHR PASSWORT NICHT GEÄNDERT HABEN:
Ihr Konto ist möglicherweise kompromittiert! Wenden Sie sich sofort an den Support:
support@hudhudnest.com

Dies ist eine automatische Benachrichtigung - bitte nicht antworten.
"),

            _ => (
                "✅ Password Changed Successfully",
                $@"
Hello {userName},

YOUR PASSWORD HAS BEEN SUCCESSFULLY CHANGED
{Rule}

✅ Your account password was successfully updated on {at} UTC

Your account is now secure with your new password.

{Rule}
NEXT STEPS:
{Rule}

1. ✅ Log in with your new password
2. ✅ Review your active sessions
3. ✅ Verify all login locations are authorized

SECURITY RECOMMENDATION:
Consider enabling two-factor authentication for additional account protection.

{Rule}

IF YOU DIDN'T CHANGE YOUR PASSWORD:
Your account may be compromised! Contact support immediately:
support@hudhudnest.com

This is an automated notification - do not reply to this email.
"),
        };
    }
}
