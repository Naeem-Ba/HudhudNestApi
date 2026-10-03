# تدقيق CI/CD — مبدأ Fail-Closed (2026-10-03)

النطاق: `.github/workflows/*` كلها، وإعدادات GitHub الحية (`gh api`)، والسكربتات التي تستدعيها البوابات.
المبدأ: فشل Staging Smoke أو اختلاف الـ commit أو نقص متغير إلزامي ⇒ **لا نشر للإنتاج**.
الإصلاحات: PR [#271](https://github.com/Naeem-Ba/HudhudNestApi/pull/271) (لا workflow جديد).

## 1) CI/CD FLOW

```
PR / push ──► ci.yml            build-and-test (format, locked restore, vuln, migrations, 6 test suites, coverage)
                                secret-scan (Gitleaks) · validate-repo-hygiene
          ──► supply-chain-validation.yml
          ──► performance-validation.yml (pr profile)
          ──► production-gate.yml
                 build-test-container  (tests + static gate + image build + Trivy SBOM/scan + 5 container assertions)
                 observability-validation · redis-sentinel-ha · performance-validation (reusable)

push master / workflow_dispatch  (يضاف إلى ما سبق)
                 validate-redis-ha-topology ─┐
                 staging-smoke ──────────────┤  يحتاج: build, observability, redis-ha, sentinel
                   (config → migrate Staging → deploy hook → انتظار نفس الـ SHA + migrations + PostGIS → E2E → observability)
                 redis-staging-failover ─────┤
                 database-recovery (نسخة احتياطية جديدة + restore فعلي) ─┤
                 production-deployment-gate  [env: production-release]  ← always()، يرفض أي نتيجة ≠ success
                          │
workflow_dispatch + ref == master فقط
                 deploy-production  [env: production]
                   1) master ما زال على نفس الـ SHA   2) إعادة فحص النتائج   3) POST على deploy hook
rollback-production.yml (dispatch) [env: production-rollback]  → Render rollback إلى dep-… معروف
```

الدليل من runs حقيقية: `37111088162` (فشل رفع artifact ⇒ `Production Deployment Gate` رفض عند *Enforce successful recovery validation* وتخطّى `deploy-production`)، و`37121762374` (push على master نجح كاملًا و`deploy-production` = skipped).

## 2) FAIL-CLOSED MATRIX

مُتحقَّق منه بتشغيل نصوص الـ steps الحقيقية مع `curl` مُزيَّف (60 حالة، كلها تمر بعد الإصلاح؛ 7 تفشل على `origin/master` قبله).

| الشرط | النتيجة المطلوبة | الآلية | الحالة |
|---|---|---|---|
| Staging smoke فشل/تخطّى | لا إنتاج | `needs`+`if` في deploy-production، و`STAGING_RESULT = success` في البوابة | ✔ |
| Staging ينشر commit مختلفًا | لا إنتاج | انتظار `build-info.commitSha == github.sha` ثم timeout ⇒ exit 1 | ✔ (8 حالات) |
| master تحرّك بعد بدء الـ run | لا إنتاج | خطوة tip-of-master قبل الـ hook | ✔ (5 حالات) |
| أي متغير/سر Staging ناقص أو غير صالح | البوابة تفشل | `smoke-staging.sh --validate-only` | ✔ (7 ناقصة + 5 غير صالحة) |
| `STAGING_DATABASE_URL` ناقص | فشل قبل النشر | خطوة migrate | ✔ |
| `PRODUCTION_DEPLOY_HOOK_URL` ناقص | فشل، لا استدعاء | خطوة الـ hook | ✔ |
| `RENDER_API_KEY`/`RENDER_SERVICE_ID` ناقص أو بصيغة خاطئة | البوابة تفشل | خطوة known-good deploy | ✔ |
| Render يستطيع النشر خارج البوابة | البوابة تفشل | **جديد:** `autoDeployTrigger ∈ {off, checksPass}` والفرع `master` | ✔ تعمل — وكشفت مخالفة حية (انظر §3) |
| لا يوجد هدف rollback | البوابة تفشل | **جديد:** كان يمرر كـ "first release" | ✔ |
| نتيجة build/staging/recovery ≠ success (failure/skipped/cancelled/فارغ) | البوابة تفشل | `[ "$X" = "success" ]` | ✔ (15 حالة) |
| dispatch من غير master | لا إنتاج | `github.ref == 'refs/heads/master'` | ✔ + static gate |
| push إلى master | لا إنتاج | `event_name == 'workflow_dispatch'` | ✔ (run 37121762374) |
| Redis HA managed غير مهيأ | **يمر (skip=success)** | قرار موثّق في `docs/REDIS-HA.md`؛ الإثبات الإلزامي هو `redis-sentinel-ha` | ⚠ fail-open بالتصميم |

## 3) BYPASS RISKS

| # | الخطورة | الخطر | الدليل | الحالة |
|---|---|---|---|---|
| B1 | **P0** | **خدمة Render للإنتاج مضبوطة على `autoDeployTrigger = commit`** (الفرع `master`): كل دمج في master ينشر الإنتاج فورًا، فكل بوابة في الـ workflow استشارية. | خطوة الفحص الجديدة في run `37123307465` قرأت القيمة الحية وفشلت: `auto-deploy trigger: commit; tracked branch: master` | **مفتوح — قرار المالك** (Render ▸ Settings ▸ Build & Deploy ▸ Auto-Deploy ⇒ `Off` أو `After CI Checks Pass`). بعد دمج #271 سيبقى `Production Deployment Gate` أحمر على push حتى يُعدَّل. |
| B2 | P1 | `master` بلا branch protection ولا rulesets ولا required status checks؛ أي دفع مباشر ممكن | `GET /branches/master/protection` → 404، `rulesets` → `[]` | مفتوح — قرار المالك |
| B3 | P1 | بيئات `production`/`production-release`/`production-recovery`/`staging` بلا required reviewers ولا branch policy؛ أي workflow على أي فرع يقرأ أسرارها | `protection_rules: []`, `deployment_branch_policy: null` | مفتوح — قرار المالك |
| B4 | P1 | أسرار الإنتاج الحرجة (`DATABASE_URL`، `BACKUP_*`) على مستوى المستودع لا البيئة؛ فرع جديد فيه workflow معدّل يقرؤها | `gh api …/actions/secrets` | مفتوح — انقلها إلى `production-recovery` مع تقييد الفرع |
| B5 | P1 | بيئة `production-rollback` **غير موجودة**؛ سيُنشئها GitHub بلا حماية وبلا أسرار، والـ rollback يقرأ `PRODUCTION_BASE_URL` من `secrets` بينما هو متغير | `GET …/environments`، 404 على أسرارها | الـ workflow أُصلح (#271)؛ إنشاء البيئة مفتوح — قرار المالك |
| B6 | P2 | لا تحقق بعد نشر الإنتاج (لا مقارنة SHA ولا smoke) — `version` يتطلب Admin JWT | نهاية `deploy-production` | مفتوح؛ الاقتراح: تحقق عبر Render API (deploy بنفس `commit.id` وحالته `live`) بنقل `RENDER_*` إلى بيئة `production` |
| B7 | P2 | الصورة المُختبرة/الممسوحة (Trivy) تُبنى داخل CI بينما Render يبني من المصدر؛ الأداة المنشورة ليست نفس الـ artifact | غياب registry/publish | مقبول موثّقًا؛ يُغلق بنشر صورة بـ digest |
| B8 | P2 | `sha_pinning_required: false` في إعدادات Actions (الالتزام قائم على سكربت التحقق فقط) | `GET …/actions/permissions` | يُنصح بتفعيله |
| B9 | P3 | TOCTOU بين فحص tip-of-master واستدعاء الـ hook (ثوانٍ) | — | مقبول |
| B10 | P3 | `validate-redis-ha-topology` و`redis-staging-failover` يمرّان كـ success عند غياب المزوّد | قرار موثّق | مقبول؛ الإلزامي هو sentinel |

## 4) PATCHES (PR #271 + ما بعده)

| Workflow | Job | Step | Condition / السبب الجذري | Patch |
|---|---|---|---|---|
| `production-gate.yml` | `production-deployment-gate` | Require a previous known-good Render deploy | `previous_deploy_id` فارغ ⇒ `none-first-deploy` ثم نجاح (الإنتاج له deploys حية، فالقائمة الفارغة تعني مفتاح/معرّف خاطئ) | يفشل ما لم يكن `vars.ALLOW_FIRST_PRODUCTION_RELEASE == 'true'` |
| `production-gate.yml` | `production-deployment-gate` | نفس الخطوة | لا شيء يتحقق من أن Render لا ينشر خارج البوابة | قراءة `GET /v1/services/{id}`؛ يفشل إن لم يكن `autoDeployTrigger` ∈ {off, checksPass} أو الفرع ≠ master أو الحقول غير معروفة |
| `rollback-production.yml` | `rollback` | env `PRODUCTION_BASE_URL` | `secrets.PRODUCTION_BASE_URL` غير موجود (هو متغير) ⇒ فارغ ⇒ `require_env` يُجهض كل rollback | `vars.* \|\| secrets.*` + خطوة تفشل خارج master |
| `ci.yml` | (workflow permissions) | — | `pull-requests: write` غير مستخدم | `read` (Gitleaks يحتاج القراءة؛ الـ PR مرّ) |
| `scripts/verify-production-gate.ps1` | build-test-container ▸ Run static production gate | — | `Assert-Contains` يطابق أي نص في الملف (يكفي تعليق) | invariants لكل job بلا تعليقات: dispatch+master فقط، كل نتيجة في `if` و`needs`، لا `always()`/`||`، tip-check قبل الـ hook، `success` حرفي، مقارنة SHA في staging، بيئة rollback |
| `tests/…/SocialDistribution/*` | build-test-container ▸ Run application tests | — | Meter ثابت على مستوى العملية يلتقطه أكثر من class متوازٍ ⇒ `Assert.Single() … 2 items` (فشل run 37123307465، نجح على master بالصدفة) | الـ classes الثلاثة في collection واحدة تُنفَّذ تسلسليًا |

## 5) VERIFICATION

1. **Static gate**: يمر على الشجرة؛ 12 طفرة (deploy قابل للوصول من push، حذف نتيجة من `if`، `always()`، أي فرع، حذف tip-check، hook قبل tip-check، recovery غير مفروض، حذف فحص Render، حذف مقارنة SHA، staging DB اختياري، `continue-on-error`، بيئة rollback غير محمية) كلها **تُمسك**.
2. **مصفوفة سلوكية** (نصوص الـ steps الفعلية + `curl` مزيّف): 60/60. على `origin/master` الأصلي تفشل 7: `commit`، `autoDeploy=yes`، حقل غائب، فرع آخر، فرع مجهول، لا هدف rollback (×2).
3. **GitHub Actions الحقيقية**:
   - `37111088162`: البوابة رفضت (recovery لم ينجح) و`deploy-production` متخطّى.
   - `37121762374`: push على master — كل البوابات نجحت و`deploy-production` متخطّى.
   - `37123307465` (dispatch من فرع الـ PR): خطوة Render الجديدة قرأت الخدمة الحية وفشلت على `commit` (B1)؛ staging/recovery/observability تخطّت، و`deploy-production` لم يعمل. أُلغي الـ run بعد الحصول على الدليل (لم يُنشَر شيء للإنتاج).
   - فحوص PR #271 كلها خضراء: Build+Test، Build/Test/Container Gate (يشمل static gate الجديد والاختبار المُصلَح)، Performance، Observability، Sentinel، Secret Scan (بصلاحية `read`)، Supply Chain، Hygiene.
4. **لم يُتحقَّق منه**: تشغيل `deploy-production` فعليًا (يُنشر الإنتاج)، وrollback الحقيقي، وتفعيل required reviewers (تغيير إعدادات — لم يُنفَّذ).

## 6) خطوات المالك (لم تُنفَّذ)

```bash
R=Naeem-Ba/HudhudNestApi
# حماية master (تحقق من أسماء الـ checks أولًا؛ المستودع عام فالميزة متاحة بلا ترقية)
gh api -X PUT repos/$R/branches/master/protection --input - <<'JSON'
{"required_status_checks":{"strict":true,"contexts":["Build + Test + PostGIS Migrations","Build, Test and Container Gate","Secret Scan","Repo Hygiene Check","Validate Source Supply Chain"]},
 "enforce_admins":false,"required_pull_request_reviews":null,"restrictions":null,"allow_force_pushes":false,"allow_deletions":false}
JSON
# بيئات محمية: reviewers = معرّف المستخدم الرقمي، والنشر من master فقط
UID_=$(gh api user --jq .id)
for e in production production-release production-recovery production-rollback; do
  gh api -X PUT repos/$R/environments/$e --input - <<JSON
{"reviewers":[{"type":"User","id":$UID_}],"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}
JSON
  gh api -X POST repos/$R/environments/$e/deployment-branch-policies -f name=master
done
# متغير للـ rollback (القيمة عامة)
gh variable set PRODUCTION_BASE_URL --env production-rollback --body "https://<PRODUCTION_API_HOST>"
# ثم أضف RENDER_API_KEY و RENDER_SERVICE_ID كأسرار لبيئة production-rollback (gh secret set … --env production-rollback)
```

وفي Render ▸ خدمة الإنتاج ▸ Settings ▸ Build & Deploy ▸ Auto-Deploy: اختر `Off` (نشر عبر الـ hook فقط) أو `After CI Checks Pass`. **يجب أن يتم قبل دمج #271 أو معه**، وإلا يفشل `Production Deployment Gate` على كل push إلى master (وهذا هو السلوك الصحيح).
