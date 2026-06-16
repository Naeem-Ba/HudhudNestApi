# Ø¥Ø¹Ø¯Ø§Ø¯ CI/CD Ø§Ù„Ø¢Ù…Ù† Ù„Ù…Ø´Ø±ÙˆØ¹ PropertyApi

Ù‡Ø°Ø§ Ø§Ù„Ù…Ù„Ù ÙŠØ´Ø±Ø­ Ø§Ù„Ø­Ø²Ù…Ø© Ø§Ù„Ø¬Ø§Ù‡Ø²Ø© Ù„ØªØ­Ø¯ÙŠØ« Pipeline Ù…Ø´Ø±ÙˆØ¹ `PropertyApi` Ø§Ù„Ø­Ø§Ù„ÙŠ.

## Ù…Ø§Ø°Ø§ ØªÙ… Ø§ÙƒØªØ´Ø§ÙÙ‡ Ù…Ù† Ø§Ù„Ù…Ø´Ø±ÙˆØ¹ Ø§Ù„Ù…Ø±ÙÙˆØ¹ØŸ

ØªÙ… Ø§Ù„Ø¹Ø«ÙˆØ± Ø¹Ù„Ù‰ Ø§Ù„Ø¹Ù†Ø§ØµØ± Ø§Ù„ØªØ§Ù„ÙŠØ© Ø¯Ø§Ø®Ù„ Ø§Ù„Ù…Ø´Ø±ÙˆØ¹:

- Solution: `PropertyApi.sln`
- Backend API: `PropertyApi/PropertyApi.csproj`
- Dockerfile: `PropertyApi/Dockerfile`
- Migrator: `tools/PropertyApi.Migrator/PropertyApi.Migrator.csproj`
- Ø§Ø®ØªØ¨Ø§Ø±Ø§Øª Ù…ØªØ¹Ø¯Ø¯Ø© ØªØ­Øª `tests/`
- CI Ø­Ø§Ù„ÙŠ ÙŠØ³ØªØ®Ø¯Ù… PostGIS ÙˆRedis ÙˆGitleaks ÙˆRepo Hygiene
- Health endpoint ÙØ¹Ù„ÙŠ: `/health`
- Login endpoint ÙØ¹Ù„ÙŠ: `/api/Auth/login`

Ù„Ù… ÙŠØªÙ… Ø§Ù„Ø¹Ø«ÙˆØ± Ø¹Ù„Ù‰ Ù…Ø´Ø±ÙˆØ¹ Angular Ø¯Ø§Ø®Ù„ Ù‡Ø°Ø§ Ø§Ù„Ù…Ù„Ù Ø§Ù„Ù…Ø±ÙÙˆØ¹. Ù„Ø°Ù„Ùƒ Ù‡Ø°Ø§ Ø§Ù„Ù€ Pipeline Ù…Ø®ØµØµ Ù„Ù„Ù€ Backend Ø§Ù„Ø­Ø§Ù„ÙŠ. Ø¥Ù† ÙƒØ§Ù† Ø§Ù„Ù€ Frontend ÙÙŠ Repository Ø¢Ø®Ø±ØŒ Ø§Ø¬Ø¹Ù„ Ù„Ù‡ Pipeline Ù…Ù†ÙØµÙ„Ù‹Ø§ Ø£Ùˆ Ø£Ø¶Ù job Ù…Ù†ÙØµÙ„Ù‹Ø§ Ø¨Ø¹Ø¯ Ø±ÙØ¹ Ù…Ø¬Ù„Ø¯ Angular Ø¥Ù„Ù‰ Ù†ÙØ³ Ø§Ù„Ù€ Repository.

## Ø§Ù„Ù…Ù„ÙØ§Øª Ø§Ù„Ù…Ø¶Ø§ÙØ© Ø£Ùˆ Ø§Ù„Ù…Ø³ØªØ¨Ø¯Ù„Ø©

Ø§Ù†Ø³Ø® Ù…Ø­ØªÙˆÙ‰ Ù‡Ø°Ù‡ Ø§Ù„Ø­Ø²Ù…Ø© Ø¥Ù„Ù‰ Ø¬Ø°Ø± Repository Ø§Ù„Ø®Ø§Øµ Ø¨Ù€ `PropertyApi`:

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

Ø§Ù„Ù…Ù„Ù `.github/workflows/ci.yml` Ù…ØµÙ…Ù… ÙƒØ¨Ø¯ÙŠÙ„ Ù…Ø¨Ø§Ø´Ø± Ù„Ù„Ù€ CI Ø§Ù„Ø­Ø§Ù„ÙŠØŒ ÙˆÙ„ÙŠØ³ ÙƒÙ…Ù„Ù Ø¥Ø¶Ø§ÙÙŠ Ù…ÙƒØ±Ø±.

## Ù…Ø§ Ø§Ù„Ø°ÙŠ ÙŠÙØ¹Ù„Ù‡ Ø§Ù„Ù€ PipelineØŸ

### 1. Code format gate

ÙŠØ´ØºÙ„:

```bash
dotnet format PropertyApi.sln --verify-no-changes --no-restore
```

Ø¥Ø°Ø§ ÙƒØ§Ù† Ù‡Ù†Ø§Ùƒ Ù…Ù„Ù ÙŠØ­ØªØ§Ø¬ formattingØŒ ØªÙØ´Ù„ Ø§Ù„Ù€ Job ÙˆÙ„Ø§ ÙŠÙ†Ø¬Ø­ Ø§Ù„Ù€ PR.

### 2. NuGet package vulnerability scan

ÙŠØ´ØºÙ„ Ø³ÙƒØ±Ø¨Øª:

```powershell
./ci/check-dotnet-vulnerabilities.ps1 -SolutionPath 'PropertyApi.sln' -FailOnSeverity High
```

Ø§Ù„Ø³ÙƒØ±Ø¨Øª ÙŠÙØ´Ù„ Ø¹Ù†Ø¯ ÙˆØ¬ÙˆØ¯ vulnerability Ø¨Ù…Ø³ØªÙˆÙ‰ `High` Ø£Ùˆ `Critical` ÙÙŠ direct Ø£Ùˆ transitive packages.

### 3. Dependency Review Ù„Ù„Ù€ Pull Requests

ÙŠØ³ØªØ®Ø¯Ù…:

```yaml
uses: actions/dependency-review-action@v4
with:
  fail-on-severity: high
```

Ù‡Ø°Ø§ ÙŠÙØ´Ù„ Pull Request Ø¥Ø°Ø§ Ø£Ø¯Ø®Ù„ dependency Ø¬Ø¯ÙŠØ¯Ø© ÙÙŠÙ‡Ø§ vulnerability Ø¨Ù…Ø³ØªÙˆÙ‰ High Ø£Ùˆ Critical.

### 4. Build + PostGIS + EF migrations + Tests

ÙŠØ­Ø§ÙØ¸ Ø¹Ù„Ù‰ Ø§Ù„Ù…ÙˆØ¬ÙˆØ¯ ÙÙŠ CI Ø§Ù„Ø­Ø§Ù„ÙŠ:

- PostgreSQL/PostGIS service
- Redis service
- Ø¥Ù†Ø´Ø§Ø¡ Extensions: `postgis`, `pg_trgm`
- ØªØ´ØºÙŠÙ„ Migrator Ø¶Ø¯ Ù‚Ø§Ø¹Ø¯Ø© CI
- ØªØ´ØºÙŠÙ„ ÙƒÙ„ Ø§Ù„Ø§Ø®ØªØ¨Ø§Ø±Ø§Øª

### 5. Coverage threshold

ÙŠØ´ØºÙ„:

```bash
dotnet test PropertyApi.sln --collect:"XPlat Code Coverage"
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"coverage-report" -reporttypes:"JsonSummary;HtmlInline_AzurePipelines"
./ci/check-coverage.ps1 -SummaryPath './coverage-report/Summary.json' -Threshold 80
```

Ø¥Ø°Ø§ ÙƒØ§Ù†Øª line coverage Ø£Ù‚Ù„ Ù…Ù† 80% ÙŠÙØ´Ù„ Ø§Ù„Ù€ Pipeline.

### 6. Docker build + Trivy scan

ÙŠØ¨Ù†ÙŠ image Ù…Ù†:

```bash
docker build -f PropertyApi/Dockerfile -t ghcr.io/<owner>/<repo>/propertyapi:<sha> .
```

Ø«Ù… ÙŠØ´ØºÙ„ Trivy ÙˆÙŠÙØ´Ù„ Ø¹Ù†Ø¯ ÙˆØ¬ÙˆØ¯ `HIGH` Ø£Ùˆ `CRITICAL` vulnerabilities:

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

Ø¹Ù†Ø¯ push Ø¥Ù„Ù‰ `main` Ø£Ùˆ `master` ÙÙ‚Ø·ØŒ ÙŠØªÙ… Ø±ÙØ¹ image Ø¥Ù„Ù‰ GitHub Container Registry:

```text
ghcr.io/<owner>/<repo>/propertyapi:<commit-sha>
```

### 8. Deploy Ø¥Ù„Ù‰ Staging

Ø¨Ø¹Ø¯ Ù†Ø¬Ø§Ø­ CI ÙˆDocker scanØŒ ÙŠØªÙ…:

1. ØªØ´ØºÙŠÙ„ migrations Ø¶Ø¯ Staging database.
2. ØªØ·Ø¨ÙŠÙ‚ Kubernetes manifest Ø§Ù„Ø®Ø§Øµ Ø¨Ù€ Staging.
3. Ø§Ù†ØªØ¸Ø§Ø± Ù†Ø¬Ø§Ø­ rollout.
4. ØªØ´ØºÙŠÙ„ smoke tests.

### 9. Smoke tests Ø¨Ø¹Ø¯ Staging

Ø§Ù„Ø³ÙƒØ±Ø¨Øª `ci/smoke-tests.sh` ÙŠÙØ­Øµ:

- `GET /health`
- `POST /api/Auth/login` ÙÙ‚Ø· Ø¥Ø°Ø§ ØªÙ… Ø¶Ø¨Ø· `STAGING_SMOKE_EMAIL` Ùˆ`STAGING_SMOKE_PASSWORD`

Ù„Ù… Ø£Ø¶Ø¹ Ø¨ÙŠØ§Ù†Ø§Øª login ÙˆÙ‡Ù…ÙŠØ© Ù„Ø£Ù† Ø§Ù„Ù…Ø´Ø±ÙˆØ¹ Ù„Ø§ ÙŠØ­ØªÙˆÙŠ Ø¹Ù„Ù‰ Ø­Ø³Ø§Ø¨ Ø§Ø®ØªØ¨Ø§Ø± Ù…Ø¶Ù…ÙˆÙ†.

### 10. Manual approval Ù‚Ø¨Ù„ Production

ØªÙ… ØªÙ†ÙÙŠØ°Ù‡ Ø¨Ø§Ù„Ø·Ø±ÙŠÙ‚Ø© Ø§Ù„Ø±Ø³Ù…ÙŠØ© Ø¯Ø§Ø®Ù„ GitHub Actions Ø¹Ø¨Ø± Environment Ø¨Ø§Ø³Ù…:

```text
production
```

ÙŠØ¬Ø¨ Ø¶Ø¨Ø· Required reviewers ÙÙŠ GitHub UIØŒ Ù…Ø«Ù„ Technical Lead Ø£Ùˆ CTO. Ù„Ø§ Ø£Ù†ØµØ­ Ø¨Ø§Ø³ØªØ®Ø¯Ø§Ù… Action Ø®Ø§Ø±Ø¬ÙŠ Ù„Ù„Ù…ÙˆØ§ÙÙ‚Ø© Ø§Ù„ÙŠØ¯ÙˆÙŠØ© Ø¹Ù†Ø¯Ù…Ø§ ØªÙƒÙˆÙ† GitHub Environments ÙƒØ§ÙÙŠØ© ÙˆØ£ÙƒØ«Ø± Ø£Ù…Ø§Ù†Ù‹Ø§.

### 11. Health check Ø¨Ø¹Ø¯ Production deploy

Ø§Ù„Ø³ÙƒØ±Ø¨Øª `ci/health-check.sh` ÙŠÙØ­Øµ:

- `GET /health`
- login Ø§Ø®ØªÙŠØ§Ø±ÙŠ Ø¥Ø°Ø§ ØªÙ… Ø¶Ø¨Ø· `PRODUCTION_HEALTH_EMAIL` Ùˆ`PRODUCTION_HEALTH_PASSWORD`

Ø¥Ø°Ø§ ÙØ´Ù„ Ø§Ù„ÙØ­ØµØŒ ØªÙØ´Ù„ Ø§Ù„Ù€ Job. ÙˆØ¥Ø°Ø§ ÙƒØ§Ù† `ALERT_WEBHOOK_URL` Ù…Ø¶Ø¨ÙˆØ·Ù‹Ø§ØŒ ÙŠØªÙ… Ø¥Ø±Ø³Ø§Ù„ ØªÙ†Ø¨ÙŠÙ‡ Ø¥Ù„Ù‰ webhook Ù…Ø«Ù„ Slack Ø£Ùˆ Teams.

## GitHub Secrets Ø§Ù„Ù…Ø·Ù„ÙˆØ¨Ø©

### Staging Environment Secrets

Ø£Ø¶ÙÙ‡Ø§ Ø¯Ø§Ø®Ù„ GitHub Environment Ø¨Ø§Ø³Ù… `staging`:

```text
STAGING_KUBE_CONFIG_B64
STAGING_DATABASE_CONNECTION_STRING
STAGING_REDIS_CONNECTION_STRING
STAGING_JWT_ISSUER
STAGING_JWT_AUDIENCE
STAGING_JWT_KEY
STAGING_OTP_SECRET_KEY
STAGING_KNOWN_PROXY
STAGING_SMOKE_EMAIL       Ø§Ø®ØªÙŠØ§Ø±ÙŠ
STAGING_SMOKE_PASSWORD    Ø§Ø®ØªÙŠØ§Ø±ÙŠ
```

### Staging Environment Variables

```text
STAGING_API_BASE_URL
STAGING_CORS_ORIGIN
```

### Production Environment Secrets

Ø£Ø¶ÙÙ‡Ø§ Ø¯Ø§Ø®Ù„ GitHub Environment Ø¨Ø§Ø³Ù… `production`:

```text
PRODUCTION_KUBE_CONFIG_B64
PRODUCTION_DATABASE_CONNECTION_STRING
PRODUCTION_REDIS_CONNECTION_STRING
PRODUCTION_JWT_ISSUER
PRODUCTION_JWT_AUDIENCE
PRODUCTION_JWT_KEY
PRODUCTION_OTP_SECRET_KEY
PRODUCTION_KNOWN_PROXY
PRODUCTION_HEALTH_EMAIL       Ø§Ø®ØªÙŠØ§Ø±ÙŠ
PRODUCTION_HEALTH_PASSWORD    Ø§Ø®ØªÙŠØ§Ø±ÙŠ
ALERT_WEBHOOK_URL             Ø§Ø®ØªÙŠØ§Ø±ÙŠ
```

### Production Environment Variables

```text
PRODUCTION_API_BASE_URL
PRODUCTION_CORS_ORIGIN
```

## Ø·Ø±ÙŠÙ‚Ø© Ø¥Ù†Ø´Ø§Ø¡ KUBE_CONFIG_B64

Ø¹Ù„Ù‰ Ø¬Ù‡Ø§Ø²Ùƒ:

```bash
base64 -w 0 ~/.kube/config
```

Ø¹Ù„Ù‰ PowerShell:

```powershell
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content $env:USERPROFILE\.kube\config -Raw)))
```

Ø¶Ø¹ Ø§Ù„Ù†Ø§ØªØ¬ ÙÙŠ secret Ø§Ù„Ù…Ù†Ø§Ø³Ø¨.

## Ø¥Ø¹Ø¯Ø§Ø¯ Manual Approval ÙÙŠ GitHub

1. Ø§ÙØªØ­ Repository Ø¹Ù„Ù‰ GitHub.
2. Settings.
3. Environments.
4. Ø£Ù†Ø´Ø¦ Environment Ø¨Ø§Ø³Ù… `production`.
5. ÙØ¹Ù‘Ù„ Required reviewers.
6. Ø£Ø¶Ù Technical Lead Ø£Ùˆ CTO Ø£Ùˆ team Ù…Ø®ØµØµ.
7. ÙØ¹Ù‘Ù„ Prevent self-review Ø¥Ù† ÙƒØ§Ù† Ù…ØªØ§Ø­Ù‹Ø§.

Ø¨Ù‡Ø°Ø§ Ø³ÙŠØªÙˆÙ‚Ù job `deploy-production` Ø­ØªÙ‰ ØªØªÙ… Ø§Ù„Ù…ÙˆØ§ÙÙ‚Ø© Ø§Ù„ÙŠØ¯ÙˆÙŠØ©.

## Branch protection Ø§Ù„Ù…Ø·Ù„ÙˆØ¨

Ø­ØªÙ‰ Ù„Ø§ ÙŠØªÙ… merge PR ØºÙŠØ± Ù†Ø§Ø¬Ø­ØŒ ÙØ¹Ù‘Ù„ Branch Protection Ø¹Ù„Ù‰ `main` Ø£Ùˆ `master` ÙˆØ§Ø¬Ø¹Ù„ Ù‡Ø°Ù‡ Checks Ù…Ø·Ù„ÙˆØ¨Ø©:

```text
Quality Gate - Format, Build, Test, Coverage, NuGet Audit
Dependency Review - Pull Request Supply Chain Gate
Secret Scan
Repo Hygiene Check
Docker Build, Trivy Scan, Push Image
```

Ù…Ù„Ø§Ø­Ø¸Ø©: GitHub Actions ÙŠÙØ´Ù„ Ø§Ù„Ù€ JobØŒ Ù„ÙƒÙ† Ù…Ù†Ø¹ merge ÙŠØªØ·Ù„Ø¨ Ø¶Ø¨Ø· Branch Protection Ù…Ù† Ø¥Ø¹Ø¯Ø§Ø¯Ø§Øª GitHub.

## Ø£ÙˆØ§Ù…Ø± Ø§Ø®ØªØ¨Ø§Ø± Ù…Ø­Ù„ÙŠØ© Ù‚Ø¨Ù„ push

Ù…Ù† Ø¬Ø°Ø± Ø§Ù„Ù…Ø´Ø±ÙˆØ¹:

```powershell
dotnet restore PropertyApi.sln
dotnet format PropertyApi.sln --verify-no-changes --no-restore
dotnet build PropertyApi.sln -c Release --no-restore
dotnet test PropertyApi.sln -c Release --collect:"XPlat Code Coverage"
./ci/check-dotnet-vulnerabilities.ps1 -SolutionPath 'PropertyApi.sln' -FailOnSeverity High
```

Ù„Ø¨Ù†Ø§Ø¡ Docker image Ù…Ø­Ù„ÙŠÙ‹Ø§:

```bash
docker build -f PropertyApi/Dockerfile -t propertyapi:local .
```

## Ù…Ù„Ø§Ø­Ø¸Ø§Øª Ù…Ù‡Ù…Ø© Ù„Ù„ØªØ·ÙˆÙŠØ±

1. Ù„Ø§ ØªØ³ØªØ®Ø¯Ù… credentials ÙˆÙ‡Ù…ÙŠØ© ÙÙŠ smoke tests. Ø£Ù†Ø´Ø¦ Ø­Ø³Ø§Ø¨ smoke Ø­Ù‚ÙŠÙ‚ÙŠ Ù…Ø­Ø¯ÙˆØ¯ Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª ÙÙŠ Staging.
2. Ù„Ø§ ØªØ¬Ø¹Ù„ migrations Ø§Ù„Ù…Ø¯Ù…Ø±Ø© ØªØ¹Ù…Ù„ ØªÙ„Ù‚Ø§Ø¦ÙŠÙ‹Ø§ Ø¯ÙˆÙ† review. Ø§Ù„Ù€ Migrator Ø§Ù„Ø­Ø§Ù„ÙŠ Ø¬ÙŠØ¯ ÙƒØ¨Ø¯Ø§ÙŠØ©ØŒ Ù„ÙƒÙ† migrations Ø§Ù„ØªÙŠ ØªØ­ØªÙˆÙŠ Ø­Ø°Ù Ø£Ø¹Ù…Ø¯Ø© Ø£Ùˆ Ø¬Ø¯Ø§ÙˆÙ„ ÙŠØ¬Ø¨ Ø£Ù† ØªÙ…Ø± Ø¨Ù…Ø±Ø§Ø¬Ø¹Ø© Ù…Ù†ÙØµÙ„Ø©.
3. Ù„Ø§ ØªØ³ØªØ®Ø¯Ù… `aquasecurity/trivy-action@master` ÙÙŠ Production pipeline. Ø§Ø³ØªØ®Ø¯Ù… Ø¥ØµØ¯Ø§Ø±Ù‹Ø§ Ø«Ø§Ø¨ØªÙ‹Ø§ØŒ ÙˆÙŠÙØ¶Ù„ Ù„Ø§Ø­Ù‚Ù‹Ø§ pin Ø¥Ù„Ù‰ SHA Ø¨Ø¹Ø¯ Ø§Ø¹ØªÙ…Ø§Ø¯Ù‡.
4. Ø¥Ø°Ø§ ÙƒØ§Ù† deploy Ø§Ù„Ø­Ù‚ÙŠÙ‚ÙŠ Ø¹Ù†Ø¯Ùƒ Ø¹Ù„Ù‰ Render ÙˆÙ„ÙŠØ³ KubernetesØŒ Ø§Ø­ØªÙØ¸ Ø¨ÙƒÙ„ CI ÙˆDocker/Trivy/CoverageØŒ ÙˆØ§Ø³ØªØ¨Ø¯Ù„ Jobs Ø§Ù„Ø®Ø§ØµØ© Ø¨Ù€ `kubectl` Ø¨Ø®Ø·ÙˆØ§Øª Render deploy hook Ø£Ùˆ API.
5. Ø¥Ø°Ø§ Ø£Ø¶ÙØª Angular Ø¯Ø§Ø®Ù„ Ù†ÙØ³ RepositoryØŒ Ø£Ø¶Ù job Ù…Ù†ÙØµÙ„Ù‹Ø§ Ù„Ù€ `npm ci`, `npm run build`, Ùˆ`npm audit --audit-level=high`.
6. Ø¥Ø°Ø§ ÙƒØ§Ù†Øª Coverage Ø§Ù„Ø­Ø§Ù„ÙŠØ© Ø£Ù‚Ù„ Ù…Ù† 80%ØŒ Ø³ÙŠØ¨Ø¯Ø£ Ø§Ù„Ù€ Pipeline Ø¨Ø§Ù„ÙØ´Ù„. Ù‡Ø°Ø§ ØµØ­ÙŠØ­ Ù…Ù† Ù†Ø§Ø­ÙŠØ© Ø§Ù„Ø¬ÙˆØ¯Ø©ØŒ Ù„ÙƒÙ† ÙŠÙ…ÙƒÙ† Ø±ÙØ¹ Ø§Ù„Ø¹ØªØ¨Ø© ØªØ¯Ø±ÙŠØ¬ÙŠÙ‹Ø§: 60 Ø«Ù… 70 Ø«Ù… 80 Ø¥Ø°Ø§ ÙƒØ§Ù† Ø§Ù„Ù…Ø´Ø±ÙˆØ¹ Ù‚Ø¯ÙŠÙ…Ù‹Ø§.
7. Package vulnerability scan Ù„Ø§ ÙŠØ¹Ù†ÙŠ Ø£Ù† ÙƒÙ„ dependency Ø¢Ù…Ù†Ø©Ø› Ù‡Ùˆ ÙÙ‚Ø· ÙŠÙØ­Øµ Ù…Ø§ Ù‡Ùˆ Ù…Ø¹Ø±ÙˆÙ ÙÙŠ Ù‚ÙˆØ§Ø¹Ø¯ advisories. ÙŠØ¬Ø¨ ØªØ­Ø¯ÙŠØ« Ø§Ù„Ø­Ø²Ù… Ø¨Ø§Ù†ØªØ¸Ø§Ù….
8. Trivy ÙŠÙØ­Øµ imageØŒ Ù„ÙƒÙ†Ù‡ Ù„Ø§ ÙŠØºÙ†ÙŠ Ø¹Ù† SAST/DAST Ù„Ø§Ø­Ù‚Ù‹Ø§.



## Coverage Threshold Baseline
Current coverage threshold is set to 20% because the measured line coverage at pipeline creation time is 22.5%. The technical target is to raise it gradually to 40%, then 60%, then 80% after adding sufficient tests.


## Coverage Threshold Baseline
Current coverage threshold is set to 20% because the measured line coverage at pipeline creation time is 22.5%. The target is to raise it gradually to 40%, then 60%, then 80% after adding sufficient tests.

