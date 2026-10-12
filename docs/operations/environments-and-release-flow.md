# البيئات (Staging / Production) ودليل النشر — HudhudNest

> آخر تدقيق: 2026-09-30. كل ما هنا مأخوذ من الملفات والـ workflows والفحص الحي؛ ما لم يمكن التحقق منه
> (لوحات Render/Netlify بلا صلاحية API) مُعلَّم بـ **[غير مُتحقَّق]** — تحقق منه يدويًا مرة واحدة.

## 1) خريطة التشغيل

| العنصر | Staging | Production |
|---|---|---|
| مستودع الـ API | `Naeem-Ba/HudhudNestApi` — branch `master` | نفسه — branch `master` (الفصل بالـ pipeline لا بالـ branch) |
| مستودع الواجهة | `Naeem-Ba/HudhudNest` — branch `staging` (يتقدّم تلقائيًا إلى `main` عبر `sync-staging.yml`) | نفسه — Netlify يبني الإنتاج من branch `release` فقط (منذ 2026-10-10)، و`main` لا ينشر الإنتاج |
| رابط الواجهة | `https://staging--hudhudnest.netlify.app` | `https://hudhudnest.com` |
| رابط الـ API | `https://propertyapi-staging-api.onrender.com` | `https://wohnungen-api.onrender.com` |
| خدمة Render | `propertyapi-staging-api` (`srv-dacpbgf40ujc73epheig`) | خدمة الإنتاج (المعرّف في سر `RENDER_SERVICE_ID`) |
| موقع Netlify | نفس موقع `hudhudnest`، فرع `staging` (Branch deploy) | نفس الموقع، السياق `production` |
| قاعدة البيانات | Render Postgres `propertyapi-staging-db` (الاسم يحوي `staging` — يفرضه `StagingEnvironmentGuard`) | قاعدة منفصلة عبر `DATABASE_URL`؛ يرفض `ProductionEnvironmentGuard` أي اسم يحوي `staging` |
| Redis | Render Valkey `hudhudnest-redis` | Upstash منفصل |
| التخزين (Cloudinary) | **نفس الـ cloud** (الخطة المجانية = بيئة واحدة). العزل الآن بمجلد جذر `staging/` تلقائيًا في Staging | جذر `hudhudnest/` بلا بادئة |
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| مشغّل النشر | خطوة `staging-smoke` في `Production Gate` عند الدمج في `master` (تهاجر القاعدة ثم Deploy Hook) | **يدوي فقط**: `workflow_dispatch` على `master` → `deploy-production` |
| الموافقة | لا | عملياً: تشغيل يدوي فقط (Required reviewers غير متاحة في الخطة الحالية، انظر §7) |
| مؤشر البيئة المرئي | شارة برتقالية `🟠 STAGING` لكل المستخدمين | شارة `🟢 PRODUCTION` للأدمن فقط |

## 2) كيف أعرف البيئة التي أنا فيها؟

**Staging**
1. الرابط `staging--hudhudnest.netlify.app`.
2. شارة برتقالية `🟠 STAGING` أسفل الشاشة (لكل المستخدمين).
3. أدمن: الشارة تعرض أيضًا `API: Staging · vX · abc1234`. إن ظهرت **حمراء** فالحزمة والـ API من بيئتين مختلفتين — توقف.
4. API: `GET /api/operational/version` بتوكن أدمن → `{"environment":"Staging","version":..,"commitSha":..}`.
   أتمتة CI: `GET /api/operational/build-info` مع الترويسة `X-Staging-Smoke-Secret` (Staging فقط).
5. Render → `propertyapi-staging-api` → آخر Deploy وcommit؛ Netlify → Deploys → فرع `staging`.

**Production**
1. الرابط `hudhudnest.com`.
2. لا شارة للمستخدم العادي. أدمن يرى `🟢 PRODUCTION` + `API: Production · vX · abc1234`.
3. API: `GET https://wohnungen-api.onrender.com/api/operational/version` بتوكن أدمن.
4. Render → خدمة الإنتاج → Deploy الحالي؛ Netlify → Production deploys.

معاينات الـ PR (`deploy-preview-N--hudhudnest.netlify.app`) تُبنى بإعداد Staging وتتصل بـ Staging API.

## 3) رفع تغيير إلى Staging (الـ API)

```powershell
git switch -c fix/my-change origin/master
# ... تعديل ...
git add <الملفات>
git commit -m "fix: ..."
git push -u origin fix/my-change
gh pr create --base master
```

- على الـ PR يعمل `Production Gate` (build + اختبارات + أداء) و`CI` — لا نشر.
- بعد الدمج في `master`: `Production Gate` يعمل كاملًا: … → `staging-smoke` (يهاجر قاعدة Staging، يستدعي Deploy Hook مثبّتًا على الـ commit المفحوص عبر `ref=<sha>` فلا يستطيع دمج لاحق استبداله، ينتظر أن يعيد `build-info` نفس الـ commit والبيئة `Staging`، ثم E2E) → استرداد القاعدة → `production-deployment-gate`.
- تابع: `gh run list --workflow "Production Gate" --branch master --limit 3` ثم `gh run watch`.
- اكتمل Staging عندما ينجح `staging-smoke`. اختبر يدويًا على الواجهة `staging--hudhudnest.netlify.app`.

**الواجهة**: افتح PR إلى `main` وجرّب معاينة الـ PR (Staging API). بعد دمجه يقدّم `sync-staging.yml` فرع `staging` تلقائيًا (fast-forward فقط) عند نجاح Frontend CI. ولو أردت ذلك يدويًا:

```powershell
git fetch origin
git push origin origin/main:staging      # staging لا يحوي أي commit فريد، فهذا fast-forward
```

## 3أ) Staging لا يراسل أشخاصًا حقيقيين

`Staging:ExternalNotificationsDisabled=true` إلزامي في Staging (`StagingEnvironmentGuard`)، وهو الآن مفروض في السلوك لا في القيمة فقط:

| القناة | سلوك Staging |
|---|---|
| البريد | أي مرسل مُعدّ (Resend/Smtp/Console) يُغلَّف بـ `StagingRecipientFilteringEmailSender`: يصل البريد فقط إلى `Staging:Email:AllowedRecipients` (عناوين كاملة أو `@domain` لنطاق كامل، بلا حساسية لحالة الأحرف). **القائمة الفارغة = لا يُرسَل شيء.** المحجوب يُسجَّل بموضوعه بلا عنوان ولا نص |
| SMS | `Staging:TestSupport:Enabled=true` ← `StagingSmokeSmsSink` (يقبل بادئة الاختبار فقط). غير ذلك ← `StagingSuppressedSmsService` (لا يرسل ويعيد فشلًا) |
| Telegram / WhatsApp | غير مسجَّلة في Staging إطلاقًا |

لاستلام بريد حقيقي في Staging أثناء الاختبار اليدوي: في Render خدمة Staging اضبط `Staging__Email__AllowedRecipients__0=you@example.com` (و`__1` ... لمزيد). لا يلزم أي تغيير في الإنتاج.

## 4) نقل الإصدار إلى Production

1. تأكد أن آخر `Production Gate` على `master` أخضر (كل الـ jobs وخاصة `staging-smoke`) **لنفس الـ commit**.
2. GitHub → Actions → **Production Gate** → Run workflow → Branch: **`master`**.
   (من أي branch آخر لن تعمل `deploy-production`.)
3. `deploy-production` يتحقق أن `github.sha` ما زال رأس `master` (وإلا يفشل لأن الـ Hook ينشر رأس master الحالي لا الـ SHA)، ثم يستدعي `PRODUCTION_DEPLOY_HOOK_URL`.
4. تحقق بعد النشر: Render → نشر ناجح؛ ثم `GET /api/operational/version` (أدمن) وقارن `commitSha` بالـ commit الذي اختبرته في Staging (`git rev-parse origin/master`).
5. الواجهة: دمج PR إلى `main` **لا** ينشر Production. الإنتاج يُبنى من فرع `release` فقط: يُقدَّم بـ PR `main` → `release` أو بـ workflow `promote-release.yml` (fast-forward إلى commit نجح فيه Frontend CI)، ثم تضغط **Publish deploy** يدويًا في Netlify (Auto Publishing مقفل عمدًا؛ كل بناء إنتاج يستهلك رصيد Netlify فتُجمَّع التغييرات). بعد النشر شغّل `npm run check:live`. الواجهة لا تمر عبر Staging؛ جرّب معاينة الـ PR أولًا.

## 5) Rollback

- API: Actions → **Rollback Production Application** (يتطلب `dep-…` معروفًا سليمًا، رقم الحادثة، اسم الموافق، وتعطيل Auto-Deploy أولًا). التفاصيل: `docs/operations/application-rollback-runbook.md`. لا يوجد downgrade لمهاجرات EF (المهاجرات إضافية فقط).
- الواجهة: Netlify → Deploys → اختر النشر السليم → **Publish deploy**.

## 6) لا تفعل

- لا تضع `DATABASE_URL`/Redis/مفاتيح Staging في خدمة الإنتاج ولا العكس (الإنتاج يرفض الإقلاع بإعداد يحوي `staging`؛ Staging يرفضه إن لم يحوِ اسم القاعدة علامة `staging`).
- لا تضبط متغير Netlify `API_URL` بنطاق **All contexts** بقيمة Staging — هذا ما كسر الإنتاج (انظر §8). اضبطه للسياقين `Deploy Previews` و`Branch deploys` فقط. الآن يفشل البناء عندما يخالف `CONTEXT` قيمة `API_URL`.
- لا تفترض أنك على Staging لأنك محلي: `npm start` يستخدم إعداد Development.
- لا تشغّل `Production Gate` يدويًا من فرع غير `master` ولا تنشر Production قبل نجاح `staging-smoke`.
- لا تحذف `Staging__*` guards ولا `production-deployment-gate` لأنها تعيق تنفيذًا.

## 7) الفجوات المعروفة (لم تُغلق)

| # | الفجوة | الخطورة | الحالة |
|---|---|---|---|
| 1 | حماية `master` غير مفعّلة وبيئات GitHub بلا Required reviewers. **تحقق حي 2026-10-03:** المستودع صار **عامًا** (`visibility: public`) فسبب «الخطة لا تدعم» لم يعد قائمًا؛ `GET /branches/master/protection` → 404، `rulesets` → `[]`، والبيئات `production`/`production-release`/`production-recovery`/`staging` بلا `protection_rules` وبلا `deployment_branch_policy`، والبيئة `production-rollback` **غير موجودة** (سيُنشئها GitHub بلا حماية عند أول تشغيل). أسرار الإنتاج الحرجة (`DATABASE_URL`, مفاتيح النسخ الاحتياطي) على مستوى المستودع فيقرؤها أي workflow على أي فرع | P1 | يتطلب قرار المالك — الأوامر في تقرير تدقيق CI/CD؛ التخفيف المؤقت: `workflow_dispatch` فقط + `master` فقط + فحص tip-of-master |
| 2 | Render الإنتاج قد يكون بـ Auto-Deploy "بعد نجاح فحوص CI" على `master` (يذكره `application-rollback-runbook.md`) — عندها الدمج قد ينشر بلا أمر يدوي | P1 | **صار مفروضًا آليًا (2026-10-03):** `production-deployment-gate` يقرأ خدمة Render ويفشل إن لم يكن `autoDeployTrigger` ∈ {`off`,`checksPass`} أو كان الفرع المتتبَّع غير `master`. يبقى قرار المالك: `Off` (نشر يدوي فقط) أم `checksPass` (نشر تلقائي بعد نجاح الفحوص) |
| 3 | خدمتا Render (Staging والإنتاج) تتبعان `master`؛ الفصل بالـ pipeline لا بالفرع | P2 | مقبول مع الحارس أعلاه |
| 4 | نشر الواجهة للإنتاج (`release`) لا يمر بـ Staging | P2 | التخفيف: معاينة الـ PR على Staging API + نشر يدوي |
| 5 | فرع الواجهة `staging` متأخر عن `main` | P2 | **أُغلقت (2026-10-12):** `sync-staging.yml` يقدّمه تلقائيًا؛ عند الفحص كان `staging` = `main` |
| 6 | Cloudinary cloud مشترك؛ صور Staging القديمة (قبل هذا التعديل) قد تكون في مجلدات الإنتاج | P2 | جديد الرفع معزول؛ نظّف القديم يدويًا |
| 7 | `Staging:ExternalNotificationsDisabled` كان يُتحقَّق من قيمته فقط ولا يؤثر في أي مرسل | P2 | **أُغلقت بالكود (QA المرحلة 3، P3-D01):** انظر §3أ. يبقى على المالك أن يضبط `Staging__Email__AllowedRecipients` إن أراد استلام بريد في Staging |
| 8 | CORS الـ Staging مضبوط على `staging--hudhudnest.netlify.app` (تحقق حي ✔) لكن ملاحظات قديمة تذكر نطاقًا سابقًا | P3 | — |

## 8) نتائج الفحص الحي (2026-09-30)

- `propertyapi-staging-api.onrender.com/health/ready` و`wohnungen-api.onrender.com/health/ready`: كلاهما `Healthy` (PostGIS + Redis).
- عزل CORS مُثبَت بـ preflight: Staging API يسمح فقط بأصل Staging؛ Production API فقط بـ `hudhudnest.com`.
- Staging: الواجهة ← Staging API ✔.
- **P0 وقع: `hudhudnest.com` (الإنتاج) كانت تستدعي Staging API** (`API_URL` مخبوز في الحزمة) فتُحجب كل الطلبات بـ CORS؛ السبب متغير Netlify `API_URL` مطبَّق على سياق Production. الإصلاح الدائم: احذف قيمة `API_URL` لسياق Production (أو اضبطها `https://wohnungen-api.onrender.com/api`) ثم أعد نشر Production. الوقاية: `tools/lib/deploy-context-guard.mjs`.
