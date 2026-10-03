# Security Audit — HudhudNest (2026-10-03)

**النطاق:** API (`PropertyApi` repo، commit `680a877` = master) + Angular/Netlify (`HudhudNest` repo، فرع `chore/remove-legacy-names`).
**المنهج:** مراجعة كود يدوية لكل محور مطلوب + اختبارات تكامل/وحدة محلية (Postgres محلي معزول + EF InMemory) + طلبات **GET قراءة فقط** على Production (ترويسات، 404/401، health). لم تُنفَّذ أي اختبارات هجومية على Production، ولا brute-force، ولا POST.
**الأسرار:** لا توجد قيم أسرار في هذا التقرير. فُحص الشجرة الحالية وتاريخ git لملفات الأسرار (انظر القسم 4).

## 1. الملخّص

| # | الخطورة | الموضوع | الحالة |
|---|---|---|---|
| F-01 | **Medium** | `PUT /api/properties/{id}` يسمح للمالك بكتابة `ExpiresAt` ⇒ تجاوز التمديد المدفوع | مُثبت + **مُصلَح** في الفرع |
| F-02 | **Medium** | `POST /api/users/me/change-password` بلا lockout ولا rate-limit ⇒ تخمين كلمة المرور بتوكن مسروق | مُثبت + **مُصلَح** |
| F-03 | **Medium/Low** | كاش الـ security-stamp (5 د) لا يُبطَل عند تغيير/إعادة تعيين كلمة المرور، تعطيل مستخدم، تغيير دور | مُثبت + **مُصلَح** |
| F-04 | **Medium** (يحتاج تحقق حي) | الـ API لا يرى IP العميل الحقيقي (قفزات Netlify→Cloudflare→Render) ⇒ حدود المعدّل/سجلات التدقيق على IP قفزة وسيطة | **إجراء للمالك** — موثَّق أدناه، بلا تغيير كود |
| F-05 | **Medium** | الواجهة (`hudhudnest.com`) بلا CSP / X-Frame-Options / nosniff / Referrer-Policy | مُصلَح في PR الواجهة (CSP بوضع Report-Only) |
| F-06 | Low | endpoints كتابة موثَّقة بلا rate limit (رسائل، إنشاء عقار، رفع صور، avatar…) | **مُصلَح** (`user-write`، `auth-password-change`) |
| F-07 | Low | رفع مستندات الاستثمار (Admin) يثق بـ Content-Type المُرسَل فقط | **مُصلَح** (امتداد + magic bytes) |
| F-08 | Low | `/health/ready` مجهول: يكشف مكوّنات البنية ويُنفّذ استعلام DB+Redis لكل طلب بلا throttle | **مُصلَح** (status فقط + كاش 5ث) |
| F-09 | Low | بيئات `Testing`/`CI` تُفعّل stack traces + Swagger + CORS مفتوح، ولا حارس يمنعها خارج الاختبار | **مُصلَح** (`TestEnvironmentGuard`) |
| F-10 | Low | `DELETE /api/admin/users/{id}` بلا audit/actor ولا حماية من تعطيل آخر Admin/النفس | **مُصلَح** (audit + رفض تعطيل النفس) |
| F-11 | Info | `@angular/router` advisory (SSR DoS) — التطبيق بلا SSR | مُصلَح في PR الواجهة (ترقية) |
| F-12 | Info | اتصال SignalR المفتوح يبقى بعد إبطال/انتهاء التوكن؛ التوكن في query string للـ hub | مقبول/توثيق |
| F-13 | Info | `GET /api/users/{id}` يكشف قائمة أدوار أي مستخدم لأي مستخدم مسجَّل | **مُصلَح** (دور Admin يُخفى عن غير الأدمن) |

## 2. التفاصيل

### F-01 — Mass assignment لـ `ExpiresAt` (Medium) — مُصلَح

- **المكوّن:** `PropertiesController.Update` ([PropertiesController.cs:212](../../HudhudNestApi/Controllers/PropertiesController.cs)) + `UpdatePropertyCommandHandler`.
- **الدليل:** الـ command يُربط مباشرةً من جسم الطلب (`[FromBody] UpdatePropertyCommand`) فكل حقل فيه خاضع للعميل، والـ handler كان يكتب `property.ExpiresAt = request.ExpiresAt`. مدة الإعلان يجب ألا تتحرك إلا بالنشر (`ListingLifecyclePolicy.PublicationPeriod`) أو بتمديد مدفوع يؤكده الأدمن (`RequestListingExtension`/`ConfirmListingExtensionPayment`).
- **إعادة الإنتاج:** `UpdatePropertyCommandHandlerSecurityTests.Owner_cannot_move_listing_expiry_through_the_update_body` — مالك يرسل `{"expiresAt":"2099-01-01T00:00:00Z"}` لإعلان منتهٍ منذ يوم. قبل الإصلاح: `Expected 2026-10-02… / Actual 2099-01-01…` (فشل).
- **الأثر:** تمديد إعلانات مجاني وبلا حدّ، وإحياء إعلان منتهٍ دون دفع ⇒ تجاوز مصدر دخل (`ListingFees`) وتجاوز دورة حياة الإعلان (التنظيف بعد فترة السماح).
- **المعالجة الدنيا:** عدم تطبيق `request.ExpiresAt` في الـ handler (سطر واحد) + تعليق. الواجهة لا ترسل الحقل.
- **التحقق:** الاختبار أعلاه يمر بعد الإصلاح؛ `Application.Tests` 1334/1334.
- **ملاحظة:** نفس نمط ربط الـ command مباشرةً موجود في `CreatePropertyCommand` (يُفرَض `OwnerId` من الـ JWT — سليم) و`SendMessageCommand`/`MarkConversationReadCommand` (المستخدم يُؤخذ من `ICurrentUserService` — سليم). يُنصح بتحويل `UpdatePropertyCommand` إلى DTO منفصل مستقبلًا بدل الاعتماد على تجاهل الحقل.

### F-02 — change-password بلا lockout/rate-limit (Medium) — مُصلَح

- **المكوّن:** `UsersController.ChangePassword` ← `IdentityCredentialService.ChangePasswordAsync`.
- **الدليل:** `UserManager.ChangePasswordAsync` يتحقق من كلمة المرور الحالية دون زيادة `AccessFailedCount`، والـ endpoint بلا `[EnableRateLimiting]`. باقي المسارات (email login، phone login، حذف الحساب، تغيير الرقم) تحتسب lockout (5 محاولات/15 د) — هذا المسار الوحيد الشاذّ.
- **إعادة الإنتاج:** `AccountCredentialChangeSecurityTests.ChangePassword_WrongCurrentPassword_IsCountedTowardsLockout`: 5 تخمينات خاطئة ثم كلمة المرور **الصحيحة** ⇒ قبل الإصلاح `204 NoContent` (لا lockout).
- **الأثر:** من يملك access token مسروقًا (30 د) يخمّن كلمة المرور بلا حدّ، وكلمة مرور مكتشفة تبقى صالحة بعد انتهاء التوكن وإبطال refresh tokens ⇒ استيلاء دائم على الحساب.
- **المعالجة الدنيا:** في `IdentityCredentialService.ChangePasswordAsync`: رفض عند `IsLockedOut`، و`CheckPasswordAsync` ثم `AccessFailedAsync` عند الفشل، و`ResetAccessFailedCount` عند النجاح — بنفس سياسة الدخول.
- **التحقق:** الاختبار يمر؛ Integration (Auth/Security/Users/Listings) 262/262 على Postgres محلي معزول.
- **توصية إضافية (غير مطبّقة):** policy مثل `auth-password-change` (3/ساعة) لـ `me/change-password`.

### F-03 — كاش security-stamp لا يُبطَل (Medium/Low) — مُصلَح

- **المكوّن:** `CachedSecurityStampValidator` (TTL 5 د) + أماكن تدوير الـ stamp.
- **الدليل:** الإبطال الفوري (`IUserSecurityStampCacheInvalidator`) كان يُستدعى فقط في Logout وreuse-detection وحذف الحساب. تغيير كلمة المرور، reset (email/phone)، تغيير رقم الهاتف، `DisableUser`، تغيير الأدوار كانت تدوّر الـ stamp **دون** إبطال الكاش.
- **إعادة الإنتاج:** `ChangePassword_EndsTheAccessTokenImmediately` — توكن أُخذ قبل تغيير كلمة المرور ⇒ قبل الإصلاح `GET /api/users/me` يعطي `200` بعد التغيير (المتوقع 401).
- **الأثر:** نافذة حتى 5 دقائق يعمل فيها التوكن المسروق/توكن الأدمن المُخفَّض/المستخدم المعطَّل بعد "تأمين" الحساب.
- **المعالجة الدنيا:** إبطال الكاش داخل `IdentityCredentialService.UpdateSecurityStampAsync` (يغطي ChangePassword/ResetPassword/Logout/Delete)، و`AdminIdentityService` (Disable + تغيير الدور)، و`PhoneAuthenticationWorkflow` (reset + تغيير الرقم).
- **التحقق:** الاختبار يمر + Auth.Tests 293، Architecture.Tests 180، Infrastructure.Tests 216، كلها خضراء.

### F-04 — auth-proxy لا يمرّر IP العميل (Medium، يحتاج تحقق حي)

- **المكوّن:** `netlify/edge-functions/lib/auth-proxy.mjs` (`FORWARDABLE_REQUEST_HEADERS` = content-type, accept, accept-language, cookie, x-xsrf-token فقط) + مفتاح التقسيم `ip:{RemoteIp}` في `RedisRateLimitPartitionKeyResolver`.
- **الدليل:** كل `/api/auth/*` (login، register، OTP، refresh) يمرّ عبر الـ Edge Function، التي لا تمرّر `X-Forwarded-For`. الـ API يرى عنوان الـ edge، فحدود `auth-login` (10/د) و`send-otp`/`verify-otp` و`CreatedByIp`/`IpAddress` في الـ audit تُحسب على IP مشترك.
- **الأثر (إن تأكد):** مهاجم واحد يستهلك حصة 10 محاولات/د فيمنع **كل** المستخدمين من الدخول (DoS)، وسجلات التدقيق بلا قيمة. lockout الحساب يبقى فعّالًا للتخمين.
- **لماذا لا يوجد إصلاح في الكود هنا:** المسار الفعلي: المتصفح → Netlify Edge Function → `api.hudhudnest.com` (Cloudflare) → Render → Kestrel. في Production `ForwardedHeaders` مفعّل بـ `ForwardLimit=1` (يؤخذ آخر عنوان في `X-Forwarded-For`، أي ما أضافته آخر قفزة موثوقة)، فالـ API يرى عنوان قفزة Cloudflare/Render لا عنوان العميل، سواء مرّرت الـ Edge Function `X-Forwarded-For` أم لا. تمرير الترويسة من الواجهة وحده بلا أثر، والبديل الشائع (الوثوق بـ `CF-Connecting-IP`) **خطر** ما لم يكن أصل Render لا يُوصَل إلا عبر Cloudflare (النطاق `onrender.com` المباشر قابل للوصول عادةً ⇒ ترويسة قابلة للتزوير ⇒ تجاوز كامل لحدود المعدّل). لذلك لا يُغيَّر الكود دون التحقق من البنية.
- **التحقق المقترح (Staging فقط):** (1) أرسل 11 طلب `POST /api/auth/login` خاطئًا خلال دقيقة من جهازَين على شبكتَين مختلفتَين عبر الواجهة؛ إن حُجب الجهاز الثاني فالمفتاح مشترك. (2) راجع `CreatedByIp` في جدول `RefreshTokens` وعمود `IpAddress` في `AuditLogs` لحساب اختبار: هل يظهر عنوان العميل أم عنوان شبكة Cloudflare/Render؟
- **مصفوفة القرار:** (أ) إن كان أصل Render مغلقًا أمام غير Cloudflare (Authenticated Origin Pulls أو allow-list لنطاقات Cloudflare) ⇒ يُوثَق `CF-Connecting-IP` فقط عندما يكون الاتصال القادم من نطاق Cloudflare، وتُمرَّر ترويسة IP العميل من الـ Edge Function. (ب) إن لم يكن مغلقًا ⇒ أغلقه أولًا (إجراء مالك)، أو أبقِ الحدود على IP القفزة وأضف حدًّا لكل حساب/رقم هاتف (موجود فعلًا للدخول عبر lockout، وللـ OTP عبر حدود الرقم).
- **المسؤول:** المالك (لوحات Render/Cloudflare/Netlify).

### F-05 — ترويسات أمان الواجهة غائبة (Medium) — مُثبت حيًا

- **الدليل:** `curl -I https://hudhudnest.com/` ⇒ فقط `Strict-Transport-Security: max-age=31536000` (بلا includeSubDomains/preload) و`Cache-Control`. **لا** `Content-Security-Policy`، `X-Frame-Options`/`frame-ancestors`، `X-Content-Type-Options`، `Referrer-Policy`، `Permissions-Policy`. `netlify.toml` و`public/` لا يحويان `[[headers]]`/`_headers`. (الـ API نفسه سليم: CSP `default-src 'none'`, DENY, nosniff, HSTS preload.)
- **الأثر:** clickjacking ممكن على صفحات تسجيل الدخول/الإعلانات؛ لا طبقة CSP تحدّ من أثر أي XSS مستقبلي. تخفيف قائم: لا توكن في storage (access في الذاكرة، refresh في httpOnly cookie) ولا أي `innerHTML`/`bypassSecurityTrust*` في `src`.
- **المعالجة الدنيا:** `[[headers]]` في `netlify.toml`: `X-Frame-Options: DENY` (أو `frame-ancestors 'none'`)، `X-Content-Type-Options: nosniff`، `Referrer-Policy: strict-origin-when-cross-origin`، `Permissions-Policy`، و`Content-Security-Policy-Report-Only` أولًا (Google/Apple sign-in، Cloudinary، API origin) ثم فرضها. لم أطبّقه لأن الـ CSP يحتاج اختبارًا مع مزوّدي الدخول.
- **التحقق:** إعادة `curl -I` بعد النشر.

### F-06 — endpoints كتابة بلا rate limit (Low)

لا يوجد `GlobalLimiter`؛ وغير المُعلَّمة بسياسة: `POST /api/messages`, `POST /api/properties`, `POST …/images`, `POST /api/users/me/avatar`, `POST /api/users/me/change-password`, `PUT /api/properties/{id}`, favorites/saved-searches/notifications. **الأثر:** إغراق إشعارات/رسائل مستخدم، استهلاك حصة Cloudinary بحسابات مسجّلة. **المعالجة:** سياسة `user-write` عامة (مثلاً 60/د لكل مستخدم) تُطبَّق افتراضيًا، وسياسات أضيق للرسائل والرفع.

### F-07 — مستندات الاستثمار: Content-Type فقط (Low)

`AddInvestmentDocumentCommandHandler` لا يتحقق من الامتداد ولا من magic bytes (على عكس باقي المرفوعات). Admin فقط ⇒ منخفض. **المعالجة:** استخدام نفس فحص التوقيع (PDF `%PDF-`، JPEG/PNG/WEBP) والامتداد.

### F-08 — `/health/ready` مجهول (Low)

يرجع `{"checks":[{"name":"postgresql-postgis"…},{"name":"redis"…}]}` (~290ms) لأي زائر: كشف بصمة البنية + استعلام DB/Redis لكل طلب بلا throttle. **المعالجة:** إبقاء `/health/live` فقط عامًا، و`/health/ready` خلف سر/شبكة داخلية أو rate limit + كاش 5–10 ث للنتيجة.

### F-09 — بيئات Testing/CI (Low)

`ExceptionHandlingMiddleware` يكشف `ex.Message` و`StackTrace` في `Testing`/`CI`، وSwagger مفعَّل، وCORS `AllowAnyOrigin` عند غياب الإعدادات؛ `ProductionEnvironmentGuard` يعمل فقط عندما `IsProduction()`. ضبط خاطئ لـ `ASPNETCORE_ENVIRONMENT=Testing` على خدمة عامة يفتح كل ذلك. **المعالجة:** رفض الإقلاع في `Testing`/`CI` إذا كان `DATABASE_URL` أو `RENDER`/`NETLIFY` موجودًا.

### F-10 — تعطيل المستخدم من الأدمن (Low)

`AdminController.DisableUser` لا يمرّر actor ولا يكتب audit (بخلاف تغييرات الأدوار)، ولا يمنع تعطيل النفس/آخر Admin. **المعالجة:** تمرير `actorId` + `AuditActions.UserDisabled` + رفض تعطيل النفس.

### F-11 / F-12 / F-13 — معلوماتية

- **F-11:** `npm audit --omit=dev`: `@angular/router` 20.0.0–20.3.31 (SSR DoS بمعاملات matrix). التطبيق SPA بلا SSR ⇒ غير قابل للاستغلال؛ ترقية عند أول patch.
- **F-12:** اتصال `/notificationHub` يُصادَق عند الاتصال فقط (يبقى بعد انتهاء/إبطال التوكن)، والتوكن في query string (قيد SignalR المعروف). لا methods قابلة للاستدعاء من العميل، والمجموعات تُحدَّد من claim على الخادم.
- **F-13:** `GET /api/users/{id}` (أي مستخدم مسجّل) يعيد `roles` و(للوكيل) رقم الهاتف. الهاتف مقصود لوكلاء؛ الأدوار تكشف هوية الأدمن. **المعالجة:** إخفاء `Admin` من `Roles` لغير الأدمن.

## 3. ما فُحص وثبتت سلامته (مع الدليل)

| المحور | النتيجة |
|---|---|
| **JWT** | HS256، مفتاح ≥32 حرف مع `ValidateOnStart`، issuer/audience/lifetime/signing key مُتحقَّق منها، ClockSkew 30ث، تحقق security-stamp لكل طلب. `alg:none` مرفوض افتراضيًا. |
| **Refresh tokens** | 64 بايت عشوائي، SHA-256 في DB، دوران ذرّي (`RevokeIfActive`)، كشف إعادة الاستخدام ⇒ إبطال كل الجلسات + تدوير stamp + audit، كوكي HttpOnly/Secure/Partitioned على `/api/auth`، CSRF مفروض على refresh. |
| **كلمات المرور** | Identity hasher، ≥8 + رقم + حرف كبير، فحص HIBP (k-anonymity) + أنماط شائعة، lockout 5/15د، إخفاء الفرق الزمني بين "مستخدم غير موجود/خاطئ/مقفل" (`BurnOnePasswordVerification`)، رسالة فشل موحَّدة. |
| **OTP** | `RandomNumberGenerator`، HMAC-SHA256 مُخزَّن، مقارنة ثابتة الزمن، 3 محاولات/5 د، استهلاك مرة واحدة، حدّ 3 تحدّيات/ساعة مع advisory lock، **decoy challenges** لمنع تعداد الأرقام، قيود دول لمنع SMS pumping، مزوّدو Console محصورون بـ Development/Testing/CI، `StagingFixedOtpService` فقط عند تفعيل Staging TestSupport (ويحظره حارس Production). |
| **Authorization / IDOR** | `FallbackPolicy = RequireAuthenticatedUser` (default-deny؛ حتى المسارات غير المعروفة ⇒ 401 على Production). فُحص الملكية في: العقارات/الصور/النشر/التمديد/المميّز، الزيارات (كل التحولات)، الرسائل (قراءة/إرسال/وسم مقروء)، الإشعارات، المفضلة، البحث المحفوظ، طلبات الخدمة (+تاريخ/مستندات)، الحجوزات القصيرة (كل التحولات) والقوائم، الوكالات (دعوات/إزالة/نقل/شعار)، التقييمات، التحليلات، طلبات التقييم العقاري. **لا IDOR وُجد.** (أنماط 404-بدل-403 مستخدمة في SavedSearch.) |
| **أدوار/Admin** | كل متحكمات Admin/Contact/Leads/Surveys/Offers/SocialDistribution/Investments-Admin عليها `[Authorize(Roles=Admin)]`؛ التسجيل لا يقبل دورًا من العميل؛ تغييرات الأدوار مُدقَّقة. على Production: `/api/admin/users`, `/api/operational/version`, `/swagger/*`, `/metrics`, `/internal/observability/synthetic` ⇒ 401، و`/api/operational/build-info` ⇒ 404. |
| **Endpoints داخلية** | `ObservabilitySynthetic`, `AlertTest`, `StagingTestSupport`, `build-info`: بوابة Staging-only + سر بمقارنة ثابتة الزمن + 404 عند الرفض؛ `ProductionEnvironmentGuard` يمنع التفعيل في Production. |
| **SQL injection** | EF Core parametrized؛ الاستثناء الوحيد (`PropertyGeoSearchRepository`) نص SQL ثابت + معاملات `@param` + قصّ pageSize/radius/Page×PageSize. `ServiceRequestNumberGenerator` يستخدم اسم sequence ثابتًا. |
| **XSS** | لا `innerHTML`/`bypassSecurityTrust*`/`eval` في `src`؛ edge function الـ OG تهرب HTML والـ JSON-LD (`<`→`<`) و`encodeURIComponent` للـ id؛ الـ sitemap يهرب XML. |
| **رفع الملفات** | نوع + امتداد + magic bytes + حجم + عدد للصور/avatar/شعار/مستندات الخدمة/صور الإقامات؛ `UniqueFilename`، مجلد بمعرّف الكيان؛ `GetImageAsync` (SSRF محتمل) غير مستدعى من أي مسار إنتاجي. (الاستثناء: F-07). |
| **CORS** | قائمة أصول صريحة + credentials؛ لا wildcard خارج Development/Testing؛ الـ API يرفض الإقلاع بدون `Cors:AllowedOrigins`. *تنبيه:* `Cors:AllowedOriginPatterns` تُضبط في لوحة Render (غير مرئية في الريبو) — تأكد أن الـ regex مُثبَّت `^…$` ولا يطابق نطاقات خارجية. |
| **ترويسات الـ API** | CSP `default-src 'none'`, `frame-ancestors 'none'`, DENY, nosniff, no-referrer, COOP/CORP, Permissions-Policy, HSTS preload (مُتحقَّق حيًا). |
| **CSRF** | مفروض على المسارات المعتمدة على كوكي؛ مُعفى فقط عند Bearer صالح؛ 403 مع `CSRF_VALIDATION_FAILED`. |
| **معالجة الأخطاء** | Production: رسالة عامة 500؛ لا stack traces. |
| **السجلات** | لا تسجيل لـ OTP/كلمات مرور/توكنات (Console OTP لـ Dev فقط وبقناع هاتف)؛ `PiiMasking` للهاتف/البريد؛ إحداثيات الجغرافيا تُقرَّب في السجلات. |
| **الأسرار** | الشجرة الحالية: لا مفاتيح خاصة/توكنات/connection strings حقيقية (فقط قيم CI/اختبار/placeholders). تاريخ git: لا `.env`/`.pem`/keystore في الـ API repo. في repo الواجهة: `localhost-key.pem`/`localhost.pem` كانت مُتتبَّعة وأُزيلت في `d18c630` (تبقى في التاريخ — مفتاح localhost فقط؛ يُفضَّل تدوير شهادة التطوير). |
| **اعتماديات** | `dotnet list package --vulnerable --include-transitive` (API): لا شيء. `npm audit --omit=dev`: انظر F-11. |

## 4. غير مشمول / قيود

- **لم يُختبر حيًا:** حدود المعدّل، lockout، CSRF/CORS على Staging/Production (تجنّبًا للاختبارات الهجومية على Production). النتائج الحيّة اقتصرت على GET قراءة فقط.
- لم تُراجَع إعدادات لوحات Render/Netlify/Cloudflare (قيم `ForwardedHeaders`, `Cors__AllowedOriginPatterns`, WAF) — لا تظهر في الريبو.
- لم يُراجَع تطبيقا Android/iOS (Capacitor) إلا عبر الشيفرة المشتركة.
- publishers التوزيع الاجتماعي (Telegram/Facebook/Instagram) خارج النطاق (Admin-only، وسبق تدقيقها: التوكنات لا تظهر في URL/السجلات).

## 5. الإجراءات المنفَّذة في الفرع

الفرع `fix/security-audit-2026-10-03` (PR الـ API):

- `UpdatePropertyCommandHandler.cs` (+ تعليق في `UpdatePropertyCommand.cs`) — F-01
- `IdentityCredentialService.cs` — F-02، F-03
- `AdminIdentityService.cs`, `PhoneAuthenticationWorkflow.cs` — F-03
- اختبارات جديدة: `UpdatePropertyCommandHandlerSecurityTests`, `AccountCredentialChangeSecurityTests` (3 اختبارات)

نتائج التشغيل بعد الإصلاح: Application 1334/1334، Auth 293/293، Architecture 180/180، Infrastructure 216/216، Integration (Auth+Security+Users+Listings) 262/262.

## 6. تحديث التنفيذ (الإصلاحات الإضافية)

- **F-06:** سياستان جديدتان في `RateLimitingRegistration` و`RedisRateLimitingDefaults`، مطبّقتان على 13 action (رسائل، عقارات، صور، avatar، profile، short-stay، change-password).
- **F-07:** `AddInvestmentDocumentCommandHandler` يتحقق من الامتداد وmagic bytes (PDF/JPEG/PNG/WEBP). اختبار: `AddInvestmentDocumentSecurityTests` (4 حالات رفض + قبول).
- **F-08:** `HealthCheckResponseWriter` يُخرج `status` فقط؛ `ReadinessResponseCacheMiddleware` يخدم `/health/ready` من نتيجة عمرها ≤5ث. يبقى مجهولًا ويعيد 200/503 لكل المستهلكين (Render، production-gate، rollback، smoke).
- **F-09:** `TestEnvironmentGuard` يرفض الإقلاع في `Testing`/`CI` إذا وُجد `RENDER`/`RENDER_SERVICE_ID`/`RENDER_EXTERNAL_URL`.
- **F-10:** `DisableUserAsync(userId, performedByUserId, ipAddress)`: رفض تعطيل النفس (يضمن بقاء أدمن)، وسجل `AuditActions.UserDisabled`، وإبطال كاش الـ stamp.
- **F-13:** `UsersController.GetById` يحذف `Admin` من `Roles` لغير الأدمن (ولغير صاحب الحساب).
