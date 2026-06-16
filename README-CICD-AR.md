# إعداد CI/CD الآمن لمشروع PropertyApi

هذا الملف يشرح الحزمة الجاهزة لتحديث Pipeline مشروع `PropertyApi` الحالي.

## ماذا تم اكتشافه من المشروع المرفوع؟

تم العثور على العناصر التالية داخل المشروع:

- Solution: `PropertyApi.sln`
- Backend API: `PropertyApi/PropertyApi.csproj`
- Dockerfile: `PropertyApi/Dockerfile`
- Migrator: `tools/PropertyApi.Migrator/PropertyApi.Migrator.csproj`
- اختبارات متعددة تحت `tests/`
- CI حالي يستخدم PostGIS وRedis وGitleaks وRepo Hygiene
- Health endpoint فعلي: `/health`
- Login endpoint فعلي: `/api/Auth/login`

لم يتم العثور على مشروع Angular داخل هذا الملف المرفوع. لذلك هذا الـ Pipeline مخصص للـ Backend الحالي. إن كان الـ Frontend في Repository آخر، اجعل له Pipeline منفصلًا أو أضف job منفصلًا بعد رفع مجلد Angular إلى نفس الـ Repository.

## الملفات المضافة أو المستبدلة

انسخ محتوى هذه الحزمة إلى جذر Repository الخاص بـ `PropertyApi`:

```text
.github/workflows/ci.yml
.github/CODEOWNERS.example
ci/check-dotnet-vulnerabilities.ps1
ci/check-coverage.ps1
ci/smoke-tests.sh
ci/health-check.sh
deploy/k8s/staging/propertyapi-deployment.yaml
deploy/k8s/production/propertyapi-deployment.yaml
docs/CI-CD-SETUP-AR.md
```

الملف `.github/workflows/ci.yml` مصمم كبديل مباشر للـ CI الحالي، وليس كملف إضافي مكرر.

## ما الذي يفعله الـ Pipeline؟

### 1. Code format gate

يشغل:

```bash
dotnet format PropertyApi.sln --verify-no-changes --no-restore
```

إذا كان هناك ملف يحتاج formatting، تفشل الـ Job ولا ينجح الـ PR.

### 2. NuGet package vulnerability scan

يشغل سكربت:

```powershell
./ci/check-dotnet-vulnerabilities.ps1 -SolutionPath 'PropertyApi.sln' -FailOnSeverity High
```

السكربت يفشل عند وجود vulnerability بمستوى `High` أو `Critical` في direct أو transitive packages.

### 3. Dependency Review للـ Pull Requests

يستخدم:

```yaml
uses: actions/dependency-review-action@v4
with:
  fail-on-severity: high
```

هذا يفشل Pull Request إذا أدخل dependency جديدة فيها vulnerability بمستوى High أو Critical.

### 4. Build + PostGIS + EF migrations + Tests

يحافظ على الموجود في CI الحالي:

- PostgreSQL/PostGIS service
- Redis service
- إنشاء Extensions: `postgis`, `pg_trgm`
- تشغيل Migrator ضد قاعدة CI
- تشغيل كل الاختبارات

### 5. Coverage threshold

يشغل:

```bash
dotnet test PropertyApi.sln --collect:"XPlat Code Coverage"
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"coverage-report" -reporttypes:"JsonSummary;HtmlInline_AzurePipelines"
./ci/check-coverage.ps1 -SummaryPath './coverage-report/Summary.json' -Threshold 80
```

إذا كانت line coverage أقل من 80% يفشل الـ Pipeline.

### 6. Docker build + Trivy scan

يبني image من:

```bash
docker build -f PropertyApi/Dockerfile -t ghcr.io/<owner>/<repo>/propertyapi:<sha> .
```

ثم يشغل Trivy ويفشل عند وجود `HIGH` أو `CRITICAL` vulnerabilities:

```yaml
uses: aquasecurity/trivy-action@0.36.0
with:
  image-ref: ${{ env.IMAGE_REF }}
  format: table
  exit-code: '1'
  ignore-unfixed: true
  vuln-type: 'os,library'
  severity: 'CRITICAL,HIGH'
```

### 7. Push Docker image

عند push إلى `main` أو `master` فقط، يتم رفع image إلى GitHub Container Registry:

```text
ghcr.io/<owner>/<repo>/propertyapi:<commit-sha>
```

### 8. Deploy إلى Staging

بعد نجاح CI وDocker scan، يتم:

1. تشغيل migrations ضد Staging database.
2. تطبيق Kubernetes manifest الخاص بـ Staging.
3. انتظار نجاح rollout.
4. تشغيل smoke tests.

### 9. Smoke tests بعد Staging

السكربت `ci/smoke-tests.sh` يفحص:

- `GET /health`
- `POST /api/Auth/login` فقط إذا تم ضبط `STAGING_SMOKE_EMAIL` و`STAGING_SMOKE_PASSWORD`

لم أضع بيانات login وهمية لأن المشروع لا يحتوي على حساب اختبار مضمون.

### 10. Manual approval قبل Production

تم تنفيذه بالطريقة الرسمية داخل GitHub Actions عبر Environment باسم:

```text
production
```

يجب ضبط Required reviewers في GitHub UI، مثل Technical Lead أو CTO. لا أنصح باستخدام Action خارجي للموافقة اليدوية عندما تكون GitHub Environments كافية وأكثر أمانًا.

### 11. Health check بعد Production deploy

السكربت `ci/health-check.sh` يفحص:

- `GET /health`
- login اختياري إذا تم ضبط `PRODUCTION_HEALTH_EMAIL` و`PRODUCTION_HEALTH_PASSWORD`

إذا فشل الفحص، تفشل الـ Job. وإذا كان `ALERT_WEBHOOK_URL` مضبوطًا، يتم إرسال تنبيه إلى webhook مثل Slack أو Teams.

## GitHub Secrets المطلوبة

### Staging Environment Secrets

أضفها داخل GitHub Environment باسم `staging`:

```text
STAGING_KUBE_CONFIG_B64
STAGING_DATABASE_CONNECTION_STRING
STAGING_REDIS_CONNECTION_STRING
STAGING_JWT_ISSUER
STAGING_JWT_AUDIENCE
STAGING_JWT_KEY
STAGING_OTP_SECRET_KEY
STAGING_KNOWN_PROXY
STAGING_SMOKE_EMAIL       اختياري
STAGING_SMOKE_PASSWORD    اختياري
```

### Staging Environment Variables

```text
STAGING_API_BASE_URL
STAGING_CORS_ORIGIN
```

### Production Environment Secrets

أضفها داخل GitHub Environment باسم `production`:

```text
PRODUCTION_KUBE_CONFIG_B64
PRODUCTION_DATABASE_CONNECTION_STRING
PRODUCTION_REDIS_CONNECTION_STRING
PRODUCTION_JWT_ISSUER
PRODUCTION_JWT_AUDIENCE
PRODUCTION_JWT_KEY
PRODUCTION_OTP_SECRET_KEY
PRODUCTION_KNOWN_PROXY
PRODUCTION_HEALTH_EMAIL       اختياري
PRODUCTION_HEALTH_PASSWORD    اختياري
ALERT_WEBHOOK_URL             اختياري
```

### Production Environment Variables

```text
PRODUCTION_API_BASE_URL
PRODUCTION_CORS_ORIGIN
```

## طريقة إنشاء KUBE_CONFIG_B64

على جهازك:

```bash
base64 -w 0 ~/.kube/config
```

على PowerShell:

```powershell
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content $env:USERPROFILE\.kube\config -Raw)))
```

ضع الناتج في secret المناسب.

## إعداد Manual Approval في GitHub

1. افتح Repository على GitHub.
2. Settings.
3. Environments.
4. أنشئ Environment باسم `production`.
5. فعّل Required reviewers.
6. أضف Technical Lead أو CTO أو team مخصص.
7. فعّل Prevent self-review إن كان متاحًا.

بهذا سيتوقف job `deploy-production` حتى تتم الموافقة اليدوية.

## Branch protection المطلوب

حتى لا يتم merge PR غير ناجح، فعّل Branch Protection على `main` أو `master` واجعل هذه Checks مطلوبة:

```text
Quality Gate - Format, Build, Test, Coverage, NuGet Audit
Dependency Review - Pull Request Supply Chain Gate
Secret Scan
Repo Hygiene Check
Docker Build, Trivy Scan, Push Image
```

ملاحظة: GitHub Actions يفشل الـ Job، لكن منع merge يتطلب ضبط Branch Protection من إعدادات GitHub.

## أوامر اختبار محلية قبل push

من جذر المشروع:

```powershell
dotnet restore PropertyApi.sln
dotnet format PropertyApi.sln --verify-no-changes --no-restore
dotnet build PropertyApi.sln -c Release --no-restore
dotnet test PropertyApi.sln -c Release --collect:"XPlat Code Coverage"
./ci/check-dotnet-vulnerabilities.ps1 -SolutionPath 'PropertyApi.sln' -FailOnSeverity High
```

لبناء Docker image محليًا:

```bash
docker build -f PropertyApi/Dockerfile -t propertyapi:local .
```

## ملاحظات مهمة للتطوير

1. لا تستخدم credentials وهمية في smoke tests. أنشئ حساب smoke حقيقي محدود الصلاحيات في Staging.
2. لا تجعل migrations المدمرة تعمل تلقائيًا دون review. الـ Migrator الحالي جيد كبداية، لكن migrations التي تحتوي حذف أعمدة أو جداول يجب أن تمر بمراجعة منفصلة.
3. لا تستخدم `aquasecurity/trivy-action@master` في Production pipeline. استخدم إصدارًا ثابتًا، ويفضل لاحقًا pin إلى SHA بعد اعتماده.
4. إذا كان deploy الحقيقي عندك على Render وليس Kubernetes، احتفظ بكل CI وDocker/Trivy/Coverage، واستبدل Jobs الخاصة بـ `kubectl` بخطوات Render deploy hook أو API.
5. إذا أضفت Angular داخل نفس Repository، أضف job منفصلًا لـ `npm ci`, `npm run build`, و`npm audit --audit-level=high`.
6. إذا كانت Coverage الحالية أقل من 80%، سيبدأ الـ Pipeline بالفشل. هذا صحيح من ناحية الجودة، لكن يمكن رفع العتبة تدريجيًا: 60 ثم 70 ثم 80 إذا كان المشروع قديمًا.
7. Package vulnerability scan لا يعني أن كل dependency آمنة؛ هو فقط يفحص ما هو معروف في قواعد advisories. يجب تحديث الحزم بانتظام.
8. Trivy يفحص image، لكنه لا يغني عن SAST/DAST لاحقًا.
