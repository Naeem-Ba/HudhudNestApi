# دليل تشغيل النشر الآلي على وسائل التواصل — Runbook

> للمعمارية والإعدادات والحالة: [`README-AR.md`](README-AR.md).
> **قواعد ثابتة:** لا توكن/كلمة مرور في محادثة أو تذكرة أو commit أو سجل. حساب الأدمن المقصود هنا هو **حساب داخل تطبيق هدهد نيست** (بريد + كلمة مرور التطبيق)، وليس تسجيل دخول Render/GitHub.

## 0) قبل أي شيء: تطبيق الترحيلات على قاعدة بيانات الإنتاج

الترحيلات **لا تُطبَّق عند إقلاع الـAPI** (قرار موثَّق: لا نشغّل الترحيل في حاويات قابلة للتوسّع). الكود المنشور على Render يتوقع أعمدة قد لا تكون موجودة بعد.

**العَرَض:** سجل Render يكرّر `column s.LeaseUntil does not exist` من `SocialPublicationDispatchHostedService` (الترحيل `20260927104507_AddSocialPublicationLease`).

1. تحقّق من القاعدة (قراءة فقط):
   ```sql
   SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 8;
   ```
   يجب أن يظهر `20260927104507_AddSocialPublicationLease`. إن لم يظهر فالترحيلات متأخرة (وقد تتأخر قبله: `AddOtpChannelToPhoneOtpChallenges`، `MakeShortStayCoordinatesNullable`، `AddUserRatingOptionalCriteria`).
2. **نسخة احتياطية موثَّقة أولاً** (`scripts/database/backup-postgres.sh` ثم `verify-backup.sh`).
3. شغّل `tools/HudhudNestApi.Migrator` بوضع Production وإعداداته من متغيّرات البيئة فقط — الخطوات والمتغيّرات المطلوبة في [`docs/operations/production-deployment-plan-2026-09-20.md`](../operations/production-deployment-plan-2026-09-20.md) (الخطوة 3) وخطة التراجع في [`database-migration-recovery-runbook.md`](../operations/database-migration-recovery-runbook.md). سلسلة الاتصال بقاعدة Render **لا تُلصق في أي مكان**.
4. أعد الفحص: الترحيل ظاهر، وسجل Render لم يعد يعرض خطأ `LeaseUntil`.

## 1) أول نشر حقيقي على تيليغرام (قناة `@hudhudnest`)

**المتطلبات المسبقة:** البند 0 منجز؛ بوت من `@BotFather` مضاف **مشرفاً بصلاحية Post Messages** في القناة؛ PR #256 (إخفاء التوكن من السجلات) منشور قبل وضع التوكن الحقيقي.

1. **اختبار مباشر للبوت دون تطبيقنا** (يثبت صحة التوكن وصلاحية النشر؛ شغّله بنفسك، لا ترسل التوكن لأحد):
   ```powershell
   $token  = Read-Host "Bot token" -AsSecureString | ForEach-Object { [Net.NetworkCredential]::new('', $_).Password }
   $chatId = "@hudhudnest"
   Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot$token/sendMessage" `
     -Body @{ chat_id = $chatId; text = "اختبار: تعريف بهدهد نيست" }
   ```
   النتيجة المتوقعة `ok = True`. أخطاء شائعة: `401` توكن خاطئ، `chat not found` معرّف القناة خاطئ أو البوت غير مضاف، `not enough rights` البوت بلا صلاحية النشر.
2. ضع التوكن في Render: `SocialDistribution__Telegram__BotToken` (بيدك). أعد النشر.
3. سجّل الدخول لصفحة الإدارة (`/admin/social-distribution`) بحساب أدمن في التطبيق:
   - **القنوات** ← قناة جديدة: المنصة تيليغرام (الخيار الوحيد المفعَّل إن كان التوكن مضبوطاً).
     > `SocialChannel` سجل داخلي يصنّف المنصة، **وليس** قناة تيليغرام الفعلية.
   - **الحسابات** ← حساب جديد: المعرّف الخارجي `@hudhudnest` (أو الرقم `-100…` للقناة الخاصة)، النوع «قناة». لا حاجة لـ«ربط» إن كنت تعتمد التوكن المشترك من Render.
   - **قواعد التوزيع** ← قاعدة جديدة تستهدف هذا الحساب (بلا تحديد = كل العقارات، أولوية 10). **بلا قاعدة مفعَّلة لا يُنشر شيء تلقائياً.**
4. **منشور تجريبي (تعريف بالشركة) عبر النظام كاملاً** — يحتاج `PropertyId` لعقار منشور حقيقي (للمنشور اليدوي حقلا `title`/`body` يتجاوزان النص المولَّد):
   ```powershell
   $base  = "https://api.hudhudnest.com"
   $login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType "application/json" `
            -Body (@{ email = Read-Host "Email"; password = Read-Host "Password" } | ConvertTo-Json)
   $h = @{ Authorization = "Bearer $($login.accessToken)"; "Content-Type" = "application/json" }   # صالح 30 دقيقة

   $pub = Invoke-RestMethod -Method Post -Uri "$base/api/social-distribution/publications" -Headers $h -Body (@{
     propertyId = "<معرّف عقار منشور>"; socialAccountId = "<معرّف الحساب>"
     title = "هدهد نيست"; body = "منصتكم الموثوقة للعقارات في سوريا — تابعونا لآخر العروض."
     imageUrl = $null; hashtags = $null; language = "ar" } | ConvertTo-Json)
   Invoke-RestMethod -Method Post -Uri "$base/api/social-distribution/publications/$($pub.id)/queue" -Headers $h -Body '{"scheduledAt":null}'
   Invoke-RestMethod -Method Post -Uri "$base/api/social-distribution/publications/$($pub.id)/publish" -Headers $h
   ```
5. تحقّق: الرد `status = Published` مع `externalPostId`، **والمنشور ظاهر فعلاً في القناة** (تأكيد بصري). إن فشل: اقرأ `errorCode`/`errorMessage` (جدول §5).
6. للتوزيع الآلي: انشر عقاراً حقيقياً (صورة واحدة على الأقل ووصف ≥ 20 حرفاً) وانتظر حتى دقيقتين ثم راجع تبويب «المنشورات».

## 2) الإيقاف الفوري

| الحاجة | الإجراء | الأثر |
|---|---|---|
| إيقاف **منصة واحدة** فوراً بلا إعادة نشر | صفحة الإدارة ← القنوات ← **تعطيل** القناة | منشوراتها `Queued`/`Retrying` تبقى كما هي (لا محاولة ولا Dead Letter) وتُستأنف عند إعادة التفعيل |
| إيقاف **كل شيء** | Render: `SocialDistribution__Enabled=false` (يُعيد تشغيل الخدمة، ~1–2 دقيقة) | لا إنشاء ولا إرسال ولا تعديل/حذف؛ النشر اليدوي يردّ 409 |
| سحب منشور خاطئ **من المنصة** | احذفه من تطبيق المنصة نفسها | المفتاح لا يسحب المنشورات الحيّة |

أعد التفعيل بالعكس. بعد إيقاف شامل راجع العقارات التي بيعت/انتهت خلال الإيقاف (أحداث دورة الحياة لا تُعاد).

## 3) توكن مرفوض أو مسرَّب

- **رُفض من المنصة:** الحساب يصير `منتهي الصلاحية` تلقائياً ويصل تنبيه `HudhudNestSocialCredentialRejected`. أنشئ توكناً جديداً (تيليغرام: `@BotFather` ← `/revoke`)، ثم الحسابات ← «تحديث الربط» أو حدّث متغيّر Render، ثم Dead Letters ← «إعادة إدراج».
- **يُشتبه بتسريبه:** ألغِ التوكن فوراً من المنصة (لا تنتظر)، فعّل مفتاح الإيقاف، ثم اتبع الخطوة السابقة. توكن وُضع في Render قبل PR #256 اعتبره **مكشوفاً في السجلات** ودوِّره.

## 4) Dead Letters

صفحة الإدارة ← Dead Letters (المعلّقة افتراضياً):
- أصلح السبب أولاً ثم «إعادة إدراج». «تم الحل» لتسجيل معالجة بلا إعادة (مع خيار الإعادة).
- `AmbiguousOutcome` = عامل انقطع أثناء النشر ولا نعرف إن استلمته المنصة. **افحص القناة يدوياً** قبل أي إعادة، وإلا تضاعف المنشور.

## 5) أخطاء شائعة (`errorCode`)

| الرمز | المعنى | الإجراء |
|---|---|---|
| `InvalidCredentials` | توكن مرفوض (الحساب صار منتهياً) | §3 |
| `PermissionDenied` | البوت/التطبيق بلا صلاحية، أو `chat not found` | تحقق من إضافة البوت مشرفاً ومن المعرّف |
| `PlatformNotConfigured` | لا ناشر فعلي: متغيّر التوكن غير مضبوط/لم تُعد النشر | §1 خطوة 2 |
| `InvalidContent` | المنصة رفضت النص/الصورة (حدّ Caption، رابط صورة لا يُجلَب) | راجع المحتوى؛ تيليغرام caption ≤ 1024 |
| `RateLimited` / `NetworkError` / `Timeout` / `ServiceUnavailable` | عابر، يُعاد تلقائياً بـBackoff | لا شيء |
| `PropertyNotPublic` | العقار لم يعد منشوراً وقت الإرسال | متوقّع |
| `AmbiguousOutcome` | §4 | §4 |

## 6) تشخيص 401 بعد دخول ناجح

بعد نشر PR #259 كل رفض بعد التحقق من التوقيع يكتب سطراً في سجل Render: `JWT rejected after signature validation …` مع السبب (claim ناقص، حساب غير موجود/معطّل، security stamp لا يطابق). ابحث عنه مباشرة بعد تكرار الطلب الفاشل. لا يُسجَّل التوكن نفسه.

## 7) المراقبة

- لوحة الإدارة ← لوحة المعلومات: معدل النجاح، الطابور، **Dead Letters غير المحلولة**.
- مقياس `hudhudnest_social_publication_attempts_total{platform,outcome,failure_reason_category}` وتنبيها `HudhudNestSocialCredentialRejected` (page) و`HudhudNestSocialPublishFailuresElevated` (ticket).
- سطر السجل `Social distribution is DISABLED` يظهر مرة عند التحوّل فقط.
