# تقرير تنفيذ Security Hardening لمشروع PropertyApi

تاريخ التنفيذ: 2026-06-15

## النتيجة التنفيذية

تم تعديل مشروع `PropertyApi` مباشرة داخل نسخة معدلة من الملف المرفوع. التعديلات ركزت على النقاط المطلوبة:

1. Redis-based distributed rate limiting للـ endpoints الحساسة.
2. SecurityStamp cache invalidation عند logout وعند تغيير الـ security stamp.
3. CSRF strategy جاهزة ومقفلة افتراضيًا لأن التطبيق الحالي يستخدم JWT Bearer وليس cookie auth.
4. Security Headers middleware للـ API.
5. اختبارات Architecture/Auth إضافية لحراسة التغييرات.

> ملاحظة مهمة: لم يتم تشغيل `dotnet build` داخل بيئة التنفيذ لأن .NET SDK غير متوفر داخل الـ sandbox. لذلك أرفقت Patch ونسخة كاملة قابلة للتجربة محليًا.

---

## 1) Redis-based distributed rate limiting

### الملفات الجديدة

- `PropertyApi/Security/RateLimiting/RedisRateLimitingMiddleware.cs`
- `PropertyApi/Security/RateLimiting/RedisRateLimitingOptions.cs`
- `PropertyApi/Security/RateLimiting/RedisFixedWindowRateLimitPolicyOptions.cs`
- `PropertyApi/Security/RateLimiting/RedisRateLimitingDefaults.cs`
- `PropertyApi/Security/RateLimiting/RedisRateLimitPartitionKeyResolver.cs`
- `PropertyApi/Security/RateLimiting/RedisRateLimitingServiceCollectionExtensions.cs`
- `PropertyApi/Security/RateLimiting/RedisRateLimitingApplicationBuilderExtensions.cs`

### الملفات المعدلة

- `PropertyApi/Program.cs`
- `PropertyApi/PropertyApi.csproj`
- `PropertyApi/appsettings.Development.example.json`
- `PropertyApi/appsettings.Testing.json`
- `PropertyApi/Controllers/AuthController.cs`

### ماذا تغير؟

- Production يتطلب Redis. إذا لم يتم ضبط `ConnectionStrings:Redis` أو `Redis:ConnectionString` يتوقف التطبيق عند startup.
- في Testing/CI يتم استخدام in-memory limiter حتى لا تعتمد الاختبارات على Redis.
- في Redis mode يتم استخدام `UseRedisRateLimiting()` بدل `UseRateLimiter()`.
- الـ partition key يعتمد على `HttpContext.Connection.RemoteIpAddress` بعد `UseForwardedHeaders()`.
- التنفيذ يستخدم Redis Lua script ذريًا:
  - `INCR`
  - `PEXPIRE`
- عند تجاوز الحد يرجع `429 Too Many Requests` مع `Retry-After`.

### السياسات المضافة/المحمية

- `auth-login`
- `auth-register`
- `auth-password-reset`
- `auth-refresh`
- `auth-logout`
- `send-otp`
- `verify-otp`
- `contact`
- `visits`
- `reviews`

### قرار أمني مقصود

لم يتم تنفيذ distributed queue رغم وجود `QueueLimit` في الـ options. السبب: queue موزعة داخل API قد تحجز connections وتزيد أثر DoS. السلوك الحالي fail-fast ويرجع `429` مباشرة، وهذا أنسب للـ public API.

---

## 2) SecurityStamp cache invalidation

### الملفات الجديدة

- `PropertyApi.Application/Common/Interfaces/IUserSecurityStampCacheInvalidator.cs`
- `PropertyApi.Infrastructure/Identity/Services/DistributedSecurityStampCacheInvalidator.cs`
- `PropertyApi.Infrastructure/Identity/Services/SecurityStampCacheKeys.cs`

### الملفات المعدلة

- `PropertyApi.Infrastructure/DependencyInjection.cs`
- `PropertyApi.Infrastructure/Identity/Services/CachedSecurityStampValidator.cs`
- `PropertyApi.Infrastructure/Identity/Services/IdentityUserService.cs`
- `PropertyApi.Application/Auth/Commands/Logout/LogoutCommand.cs`

### ماذا تغير؟

- تم توحيد cache key عبر `SecurityStampCacheKeys.ForUser(userId)`.
- عند `UpdateSecurityStampAsync` يتم حذف cache entry من `IDistributedCache`.
- عند `LogoutCommand` يتم:
  1. إلغاء refresh token المطلوب.
  2. تحميل المستخدم.
  3. تدوير SecurityStamp عبر `UpdateSecurityStampAsync`.
  4. حذف cache entry حتى عند فشل تدوير الـ stamp كـ fail-closed behavior.

### أثر أمني مهم

Logout الآن يبطل access tokens السابقة لأنها تعتمد على security stamp داخل JWT. هذا سلوك صارم ويعني عمليًا logout من جميع الجلسات التي تحمل access token قديمًا. إذا أردت مستقبلًا logout لجهاز واحد فقط، يجب إضافة `jti` blacklist موزعة بدل تدوير SecurityStamp لكل logout.

---

## 3) CSRF strategy

### الملفات الجديدة

- `PropertyApi/Security/Csrf/CookieCsrfOptions.cs`
- `PropertyApi/Security/Csrf/CookieCsrfProtectionMiddleware.cs`
- `PropertyApi/Security/Csrf/CsrfExtensions.cs`
- `PropertyApi/Controllers/CsrfController.cs`

### الوضع الحالي

المشروع الحالي يستخدم JWT Bearer عبر `Authorization` header. لا يوجد اعتماد واضح على authentication cookies، لذلك تم إبقاء CSRF protection معطلًا افتراضيًا:

```json
"CookieCsrf": {
  "Enabled": false,
  "AuthenticationCookieName": ".AspNetCore.Identity.Application",
  "ProtectAnonymousUnsafeEndpoints": false
}
```

### عند التحول إلى cookies

فعّل:

```json
"CookieCsrf": {
  "Enabled": true,
  "AuthenticationCookieName": ".AspNetCore.Identity.Application",
  "ProtectAnonymousUnsafeEndpoints": false
}
```

ثم اجعل Angular يطلب:

```http
GET /api/security/csrf-token
```

ويُرسل header:

```http
X-XSRF-TOKEN: <token>
```

---

## 4) Security Headers

### الملفات الجديدة

- `PropertyApi/Security/Headers/SecurityHeadersOptions.cs`
- `PropertyApi/Security/Headers/SecurityHeadersMiddleware.cs`
- `PropertyApi/Security/Headers/SecurityHeadersExtensions.cs`

### Headers المضافة

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `X-Permitted-Cross-Domain-Policies: none`
- `Cross-Origin-Opener-Policy: same-origin`
- `Cross-Origin-Resource-Policy: same-origin`
- `Permissions-Policy: ...`
- `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`

### ملاحظة CSP

هذه CSP صارمة ومناسبة لـ JSON API. تم استثناء مسار `/swagger` من CSP حتى لا تنكسر Swagger UI أثناء التطوير أو عند تفعيلها مؤقتًا في Staging. في Production يفضل إبقاء Swagger غير مكشوف أو وضعه خلف VPN/Auth Gateway.

---

## 5) الاختبارات المضافة/المعدلة

### ملفات جديدة

- `tests/PropertyApi.Auth.Tests/Application/Security/DistributedSecurityStampCacheInvalidatorTests.cs`
- `tests/PropertyApi.Architecture.Tests/Api/RedisRateLimitingGuardTests.cs`
- `tests/PropertyApi.Architecture.Tests/ProductionHardening/SecurityHeadersTests.cs`

### ملفات معدلة

- `tests/PropertyApi.Architecture.Tests/Api/RateLimitingGuardTests.cs`

---

## أوامر التحقق محليًا

من جذر المشروع:

```powershell
dotnet restore
dotnet build
dotnet test
```

لتجربة Redis محليًا عبر Docker:

```powershell
docker run --name propertyapi-redis -p 6379:6379 -d redis:7-alpine
```

ثم اضبط:

```powershell
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project .\PropertyApi\PropertyApi.csproj
```

---

## ملاحظات مخاطرة

1. `ForwardedHeaders` في النسخة الأصلية يثق بالـ forwarded headers في Production عند عدم ضبط KnownProxies/KnownNetworks. هذا قد يسمح بـ IP spoofing إذا لم تكن المنصة تضبط proxy boundary بشكل صحيح. الأفضل ضبط `ForwardedHeaders:KnownProxies` أو `KnownNetworks` في Production.
2. تدوير SecurityStamp عند logout يبطل كل access tokens القديمة للمستخدم، وليس الجلسة الحالية فقط.
3. Redis rate limiting يحمي على مستوى التطبيق، لكنه ليس بديلًا عن WAF/CDN/DDoS protection أمام الـ API.
4. لم يتم تشغيل build داخل sandbox لعدم توفر .NET SDK.
