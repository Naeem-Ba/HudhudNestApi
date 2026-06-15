# اختبارات OTP / SMS / ForwardedHeaders — PropertyApi

هذا الحزمة تضيف اختبارات مخصصة للبنود الأربعة المطلوبة، مع تعديلين أمنيين صغيرين حتى تكون الاختبارات قابلة للنجاح ولا تكتفي بكشف الخلل فقط.

## الملفات المضافة

- `tests/PropertyApi.Auth.Tests/Infrastructure/OtpGenerationTests.cs`
- `tests/PropertyApi.Auth.Tests/Infrastructure/SmsHttpsOnlyTests.cs`
- `tests/PropertyApi.Integration.Tests/Auth/OtpHourlyCountingTests.cs`
- `tests/PropertyApi.Integration.Tests/Security/ForwardedHeadersTests.cs`
- `tests/PropertyApi.Integration.Tests/TestInfrastructure/TestApplication.cs`

## الملفات المعدلة

- `PropertyApi.Infrastructure/Auth/Services/OtpService.cs`
- `PropertyApi.Infrastructure/Auth/Services/SmsProviderOptions.cs`
- `PropertyApi.Infrastructure/DependencyInjection.cs`
- `PropertyApi.Infrastructure/Health/ProductionStartupValidator.cs`
- `PropertyApi/Program.cs`

## لماذا توجد تعديلات إنتاجية؟

1. **OTP generation**: تم استبدال أسلوب `Math.Abs(BitConverter...) % 1_000_000` بـ `RandomNumberGenerator.GetInt32(0, 1_000_000)` لتجنب modulo bias وتجنب حالة نادرة جدًا قد تسبب `OverflowException` عند `int.MinValue`.

2. **SMS HTTPS-only**: الكود الحالي كان يسمح بـ HTTP في Production. الاختبار الأمني الصحيح يجب أن يفشل هذا السلوك، لذلك أضيفت دالة `ValidateForEnvironment` داخل `SmsProviderOptions` وتم استخدامها في DI و `ProductionStartupValidator`.

3. **ForwardedHeaders**: الكود الحالي كان يزيل `KnownNetworks/KnownProxies` كحل افتراضي في Production عند غياب الإعدادات. هذا عملي لبعض المنصات، لكنه ليس تشديدًا أمنيًا. تم تغييره إلى fail-fast في Production، وإضافة رفض 403 عند وصول `X-Forwarded-*` من proxy غير معروف.

## أوامر التشغيل

```bash
# من جذر المشروع
dotnet restore
dotnet build

# اختبارات OTP generation
dotnet test --filter "FullyQualifiedName~OtpGenerationTests"

# اختبارات OTP hourly counting
dotnet test --filter "FullyQualifiedName~OtpHourlyCountingTests"

# اختبارات SMS HTTPS-only
dotnet test --filter "FullyQualifiedName~SmsHttpsOnlyTests"

# اختبارات ForwardedHeaders
dotnet test --filter "FullyQualifiedName~ForwardedHeadersTests"

# كل الاختبارات
dotnet test
```

## ملاحظة مهمة للإنتاج

بعد تشديد ForwardedHeaders، يجب ضبط واحد من التالي في Production:

```json
"ForwardedHeaders": {
  "ForwardLimit": 1,
  "KnownProxies": [ "YOUR_PROXY_IP" ]
}
```

أو:

```json
"ForwardedHeaders": {
  "ForwardLimit": 1,
  "KnownNetworks": [ "YOUR_PROXY_CIDR" ]
}
```

بدون هذا الإعداد سيُفشل التطبيق التشغيل في Production عمدًا، لأن الثقة بـ `X-Forwarded-*` بدون proxy معروف خطر أمني.
