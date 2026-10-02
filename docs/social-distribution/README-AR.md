# النشر الآلي على وسائل التواصل الاجتماعي — الحالة والمعمارية والإعدادات

> **آخر تحديث:** 2026-10-02. الحالة أدناه مبنية على قراءة الكود على `master` وعلى طلبات الدمج المذكورة؛ تحقّق من حالة كل PR على GitHub قبل الاعتماد عليها.
> للتشغيل اليومي والطوارئ: [`RUNBOOK-AR.md`](RUNBOOK-AR.md). للتنبيهات: [`docs/observability/alert-response-runbook.md`](../observability/alert-response-runbook.md).

## 1) ما هي الميزة

| الطبقة | الوصف | الحالة |
|---|---|---|
| **أ) المشاركة اليدوية من الزائر** | زر مشاركة، روابط Canonical، وسوم OG/Twitter، Edge Function للزواحف، تتبّع UTM | تعمل (قبل هذه الوثيقة) |
| **ب) التوزيع الآلي من حسابات المنصة نفسها** | قنوات ← حسابات ← قواعد توزيع ← منشورات ← طابور ← عامل خلفي ← Dead Letters، مع ناشرين حقيقيين لتيليغرام/فيسبوك/إنستغرام | مكتملة برمجياً، **لم تُختبَر على منصة حقيقية عبر النظام بعد** |

الطبقة (ب) تنشر باسم **حسابات علامة هدهد نيست** وليس باسم المستخدمين، لذلك كل نقاطها مقصورة على دور `Admin`.

## 2) التدفق

```
نشر عقار (إنشاء/تعديل/تمديد/PATCH publish)
        │  PropertyPublishedEvent                 ┌── شبكة أمان: Reconciliation كل دورتين
        ▼                                         │   (عقارات منشورة بلا DistributionRun خلال 3 أيام)
 DistributionEngine ◄─────────────────────────────┘
   · بوابة الأهلية (حدّ أدنى للصور/الوصف)
   · اختيار القاعدة الفائزة لكل حساب (الأكثر تحديداً ثم الأعلى أولوية)
   · لا تكرار لنفس (عقار، حساب)
        ▼
 SocialPublication (Draft → Queued) + SocialPostContent (نص/صورة/رابط UTM)
        ▼
 SocialPublicationDispatchHostedService  (كل دقيقتين، Advisory Lock، حتى 25 منشوراً/دورة)
   · يحرّر المنشورات العالقة بعد انتهاء Lease → AmbiguousOutcome (لا يُعاد تلقائياً أبداً)
   · PublishSocialPublicationCommand  ← نفس مسار النشر اليدوي
        ▼
 ISocialPublisher  (Telegram | Facebook | Instagram حقيقية — البقية Placeholder)
        ▼
 Published | Retrying (Backoff+Jitter) | Failed → Dead Letter
```

أحداث دورة الحياة (`PropertyStatusChangedEvent`، `PropertyDeletedEvent`، انتهاء الصلاحية) تُعدِّل/تعلّق/تحذف المنشور الحيّ بحسب قدرات كل منصة.

## 3) ما أُنجز وما تبقّى

| البند | الحالة | المرجع |
|---|---|---|
| إصلاحات الأساس: مدقّق الحقائق، الزناد من كل المسارات، بوابة الأهلية، Lease/Reaper، جودة النص، حدّ Caption، دورة الحياة، E2E دائم | منجز على master | #221 |
| ناشر تيليغرام الحقيقي (Bot API) | منجز على master | #222 عبر #228 |
| ناشر فيسبوك الحقيقي (Graph API) | منجز على master | #223 عبر #228 |
| ناشر إنستغرام الحقيقي (Content Publishing API) | منجز على master | #225 عبر #228 |
| حلّ بيانات الاعتماد لكل حساب (مشفَّرة، لا تُعاد في أي استجابة) | منجز على master | #226 عبر #228 |
| واجهة الإدارة: قنوات/حسابات/لوحة/منشورات | منجزة | HudhudNest#98 |
| منع تسريب توكن تيليغرام (جزء من الـURL) إلى سجلات HttpClient | بانتظار الدمج | #256 |
| تحويل صورة المنشور إلى JPEG محدود الحجم (إنستغرام JPEG فقط، تيليغرام ≤ 5MB) | بانتظار الدمج | #257 |
| مفتاح إيقاف شامل + إيقاف قناة + انتهاء صلاحية الحساب عند رفض التوكن | بانتظار الدمج | #258 |
| تسجيل سبب رفض JWT بعد التحقق (تشخيص 401) | بانتظار الدمج | #259 |
| عدّادات النشر + تنبيهان (توكن مرفوض / فشل متكرر) | بانتظار الدمج | #260 |
| `isLive` في `GET /publishers` | بانتظار الدمج | #261 |
| واجهة قواعد التوزيع + Dead Letters + حصر المنصات على الجاهزة | بانتظار الدمج | HudhudNest#112 |
| **تطبيق ترحيل `AddSocialPublicationLease` على قاعدة بيانات الإنتاج** | **مطلوب من المالك — العامل الخلفي يفشل بدونه** (`column LeaseUntil does not exist`) | [`RUNBOOK-AR.md` §0](RUNBOOK-AR.md) |
| أول نشر حقيقي عبر النظام على تيليغرام (القناة `@hudhudnest`) | لم يُنفَّذ — تحقّق المالك مباشرة من البوت فقط | [`RUNBOOK-AR.md` §1](RUNBOOK-AR.md) |
| فيسبوك/إنستغرام ضد حسابات Meta حقيقية | لم يُنفَّذ — يحتاج تطبيق Meta وصلاحيات ومراجعة (وإمكانية الوصول من سوريا غير مؤكَّدة) | تقرير 2026-09-25 §7 |
| TikTok / YouTube / LinkedIn | غير مُنفَّذة عمداً (Placeholder؛ الواجهة تعطّلها عبر `isLive`) | — |
| توليد صور بعلامة تجارية (PNG/JPEG عبر Skia أو Cloudinary Overlays) | غير مُنفَّذ — المولّد الحالي ينتج SVG ولا يُرفَق بالنشر (`AttachGeneratedAssetToAutomaticPublications=false`) | تقرير 2026-09-25 D2 |
| تتبّع تاريخ انتهاء التوكن (OAuth لـ Meta) وتجديده | غير مُنفَّذ — اليوم يُعلَّم الحساب `Expired` بعد أول رفض فقط | — |

## 4) مرجع الإعدادات (متغيّرات بيئة Render)

كل متغيّر اختياري ما لم يُذكر؛ الأسرار تُضبط **بيد المالك في Render ولا تمر عبر محادثة أو تذكرة أو commit**.

| المتغيّر | الافتراضي | الأثر |
|---|---|---|
| `SocialDistribution__Enabled` | `true` | `false` = **مفتاح الإيقاف**: لا إنشاء ولا إرسال ولا استدعاء دورة حياة (يتطلب إعادة تشغيل الخدمة) |
| `SocialDistribution__Telegram__BotToken` | فارغ | سرّ. وجوده يُفعّل الناشر الحقيقي لتيليغرام (توكن مشترك احتياطي؛ توكن الحساب الخاص له الأولوية) |
| `SocialDistribution__Facebook__PageAccessToken` | فارغ | سرّ. يُفعّل ناشر فيسبوك |
| `SocialDistribution__Instagram__AccessToken` | فارغ | سرّ. يُفعّل ناشر إنستغرام |
| `SocialDistribution__Telegram__BaseUrl` / `TimeoutSeconds` | `https://api.telegram.org/` / 15 | |
| `SocialDistribution__Facebook__ApiVersion` / `Instagram__ApiVersion` | `v21.0` | |
| `SocialDistribution__Eligibility__MinImageCount` / `MinDescriptionLength` | 1 / 20 | بوابة أهلية العقار قبل أي قاعدة |
| `SocialDistribution__ContentReview__RequireReviewForAutomaticPublications` | `false` | `true` = كل منشور آلي ينتظر موافقة بشرية (`PendingReview`) |
| `SocialDistribution__Reconciliation__Enabled` / `LookbackDays` / `MinAge` / `BatchSize` | `true` / 3 / 5 دقائق / 10 | شبكة أمان للزناد |
| `SocialDistribution__Retry__BaseDelay` / `MaxDelay` / `MaxJitterMilliseconds` | 2 دقيقة / 6 ساعات / 30000 | |
| `SocialDistribution__Brand__LogoUrl` | فارغ | شعار الصور المولَّدة (غير مستعمل ما دام الإرفاق معطّلاً) |
| `Frontend__BaseUrl` | — (مطلوب في Production) | أساس روابط الوجهة في المنشورات |

## 5) ضمانات السلامة المقصودة

- **لا نجاح مزيَّف:** الناشر غير المُعدّ يعيد `PlatformNotConfigured` دائماً ولا يختلق `ExternalPostId`.
- **لا نشر مزدوج بعد انقطاع:** الحالة `Publishing` + `LeaseUntil` تُحفَظ **قبل** النداء الخارجي؛ منشور عالق يصير `AmbiguousOutcome` ولا يُعاد تلقائياً.
- **لا توكن في السجلات:** عميل تيليغرام بلا HttpClient loggers؛ فيسبوك/إنستغرام ترسل التوكن في جسم POST لا في الـURL؛ اختبارات تلتقط كل السجلات (`SocialPublisherHttpLoggingTests`).
- **لا توكن في أي استجابة API:** `SocialAccountDto` لا يحمل حقل اعتماد، فقط `hasCredential`.
- **لا نص مختلَق:** المدقّق يرفض الأسعار والروابط غير المطابقة للعقار.
- **التراجع السريع:** مفتاح الإيقاف الشامل أو إيقاف قناة واحدة من الواجهة بلا إعادة نشر.

## 6) حدود معروفة

- ناشرو تيليغرام/فيسبوك/إنستغرام كُتبوا واختُبروا مقابل استجابات موثّقة مُحاكاة (`HttpMessageHandler`)، **لا مقابل حساب حقيقي** عبر النظام. أرجح ما قد يحتاج تعديلاً: صياغة خطأ `chat not found`، شكل `reply_parameters`، رمز Meta `9007` لحاوية لم تجهز.
- انتهاء مهلة العميل بعد أن عالج تيليغرام الطلب فعلاً قد يسبب نشراً مزدوجاً (Bot API بلا مفتاح Idempotency) — خطر موثَّق ومتبقٍّ.
- أحداث دورة الحياة التي تقع أثناء الإيقاف الشامل لا تُعاد بعد التفعيل؛ راجع المنشورات الحيّة لعقارات بيعت/انتهت خلال الإيقاف.
- المقاييس تتطلب أن يكون خط OTel/Prometheus منشوراً؛ قواعد التنبيه لم تُشغَّل عبر `promtool` محلياً (يتحقق منها workflow `observability-validation`).
