# دفتر المتطلبات التفصيلية (Pflichtenheft) — منصّة هدهد نيست (HudhudNest)

| | |
|---|---|
| **الإصدار** | 1.0 |
| **التاريخ** | 2026-10-02 |
| **الحالة** | مسوّدة معتمدة للمراجعة — مطابقة للكود على `master` عند `d153318` |
| **النمط** | Pflichtenheft وفق DIN 69901 (ردّ المتعاقد/المطوّر على الـ Lastenheft): *كيف* يحقّق النظام كل متطلّب |
| **المرجع المعماري** | [ARD v2.0](../architecture/ARD/ARD-HudhudNestApi.md) — مصدر الأرقام والقرارات |
| **الوثيقة المرافقة** | [دليل المستخدم](USER-GUIDE-AR.md) |
| **الجمهور** | المالك/صاحب المنتج، فريق التطوير، المراجعون، الجهات التي تقيّم التسليم |

### سجلّ المراجعات
| الإصدار | التاريخ | الوصف |
|---|---|---|
| 1.0 | 2026-10-02 | الإصدار الأول: متطلبات مستخلَصة من النظام القائم، بحالة تحقّق مثبتة بالدليل |

### مفتاح الحالة والأولوية

| الرمز | الحالة | المعنى |
|---|---|---|
| ✅ | مُنفَّذ | موجود في الكود ومغطّى باختبار أو بنص واضح |
| 🟡 | جزئي | موجود في الخلفية أو الواجهة فقط، أو بقيد معروف |
| ⏳ | مخطَّط | لم يُنفَّذ بعد، له خطة/وثيقة |
| ⛔ | غير مُنفَّذ | غير موجود عمدًا أو بعد |
| ❓ | غير مُتحقَّق | موجود لكن لم يُجرَّب على خدمة/بيئة حقيقية |

| الأولوية | المعنى |
|---|---|
| **M** (Muss) | إلزامي |
| **S** (Soll) | مرغوب بقوة |
| **K** (Kann) | اختياري |

> **ملاحظة منهجية:** هذه الوثيقة **تصف النظام القائم** وتضع له حالة تحقّق صادقة، فهي أيضًا خطّة الإغلاق: كل بند ليس ✅ هو فجوة مسمّاة. لا يُعتبر أي بند ✅ إلا إذا وُجد في الكود فعلًا.

---

## 1. الأهداف (Zielbestimmung)

### 1.1 هدف المنتج
منصّة رقمية موحّدة لسوق العقارات السوري تنظّم الإعلانات (بيع/إيجار/إقامة قصيرة) وتربط المالكين والمكاتب والباحثين ومزوّدي الخدمات والمستثمرين، بصور حقيقية وبيانات موثوقة وتواصل مباشر، مع حماية الخصوصية والأمان.

### 1.2 متطلبات الإلزام (Musskriterien)
| المعرّف | المتطلّب |
|---|---|
| /M01/ | تسجيل الدخول والتسجيل بالبريد والهاتف (OTP) وGoogle وApple |
| /M02/ | نشر إعلانات البيع والإيجار مع الصور والموقع، بدورة صلاحية (30 يومًا) وتمديد |
| /M03/ | البحث بالفلاتر والبحث النصي والبحث الجغرافي |
| /M04/ | الزيارات والمراسلة والتقييمات بين الأطراف |
| /M05/ | خطط اشتراك بحصص إعلانات تُفرَض في الخادم |
| /M06/ | أمان: JWT قصير + تحديث دوّار، CSRF، تحديد معدل، حماية الملفات المرفوعة |
| /M07/ | حذف الحساب وتصدير البيانات والموافقات (امتثال GDPR ومتاجر التطبيقات) |
| /M08/ | واجهة عربية RTL مع الإنجليزية والألمانية، ويب وجوال |
| /M09/ | مراقبة، نسخ احتياطي واسترجاع، بوابة إصدار آلية |

### 1.3 متطلبات الأفضلية (Sollkriterien)
/S01/ الإقامة القصيرة · /S02/ المكاتب العقارية · /S03/ سوق الخدمات · /S04/ التقييم العقاري (Valuation) · /S05/ إدارة تحديثات التطبيق · /S06/ إشعارات لحظية (SignalR) · /S07/ OTP عبر تيليغرام وواتساب · /S08/ لوحات إدارة.

### 1.4 متطلبات اختيارية (Kannkriterien)
/K01/ النشر الآلي على وسائل التواصل · /K02/ الاستثمار العقاري (اطلاع/اهتمام) · /K03/ التسويق ما قبل الإطلاق (Leads/Surveys/Offers) · /K04/ التحليلات.

### 1.5 معايير الحدّ (Abgrenzungskriterien) — ما **ليس** ضمن النطاق
| المعرّف | خارج النطاق |
|---|---|
| /A01/ | **بوابة دفع إلكتروني**: الدفع بتحويل بنكي يدوي برقم مرجع؛ لا بطاقات |
| /A02/ | **نصيحة مالية/استثمار فعلي**: الاستثمار «تعبير عن اهتمام» بلا مبلغ ولا التزام |
| /A03/ | **عزل مستأجرين صارم** بين المكاتب (ADR-006) |
| /A04/ | **ذكاء اصطناعي فعلي** في توليد محتوى النشر (قالب حتمي فقط — ADR-011) |
| /A05/ | **النشر الحي على TikTok/YouTube/LinkedIn** (Placeholder عمدًا) |
| /A06/ | **Webhooks واردة** من مزوّدين خارجيين |
| /A07/ | **WhatsApp لأرقام سوريا** (غير مدعوم من Meta) |
| /A08/ | الشيفرة الداخلية للواجهة والتطبيق الأصلي (تُغطّى من زاوية العقد فقط) |

---

## 2. مجال الاستخدام (Produkteinsatz)

### 2.1 التطبيق والبيئة
منصّة ويب (Angular SPA على Netlify) وتطبيق جوال (Capacitor: Android/iOS) تستهلكان واجهة REST/JSON واحدة (`HudhudNestApi`) وقناة SignalR للإشعارات.

### 2.2 المستخدمون والأدوار
| الفاعل | الدور التقني | الحاجة الأساسية |
|---|---|---|
| زائر | بلا دور | تصفّح، تقدير سعر، قراءة تقييمات |
| مستخدم | `User` | إعلان، زيارة، مراسلة، تقييم |
| وسيط | `Agent` | إعلانات بصفة موثّقة |
| مالك مكتب | `AgencyOwner` | إدارة مكتب وفريق |
| عضو مكتب | `AgencyAgent` | إعلاناته ضمن المكتب |
| مزوّد خدمة | ملف `ServiceProvider` | طلبات الخدمة |
| مستثمر | مستخدم مسجَّل | مراقبة وتسجيل اهتمام |
| مسؤول | `Admin` | الإدارة والتشغيل |

### 2.3 شروط التشغيل
خدمة متاحة 24/7 بهدف توفر 99.5% (SLO)؛ بيئتان: Staging وProduction (القسم 11).

---

## 3. نظرة عامة على المنتج (Produktübersicht)

### 3.1 سياق النظام
انظر [مخطط السياق](../architecture/ARD/diagrams/01-system-context.puml) (مصدر محدَّث لـ v2.0).

### 3.2 حالات الاستخدام الرئيسية
| المعرّف | حالة الاستخدام | الفاعل |
|---|---|---|
| UC-01 | التسجيل والدخول واستعادة كلمة المرور | زائر |
| UC-02 | نشر إعلان وتمديده | مستخدم |
| UC-03 | البحث ومتابعة عقار (مفضّلة/بحث محفوظ) | زائر/مستخدم |
| UC-04 | طلب زيارة ومتابعتها وتقييم العقار | مستخدم/مالك |
| UC-05 | مراسلة المالك | مستخدم/مالك |
| UC-06 | حجز إقامة قصيرة وإدارتها | ضيف/مضيف |
| UC-07 | طلب خدمة وتنفيذها | مالك/مزوّد |
| UC-08 | طلب تقييم سعر | زائر/مستخدم + مكاتب |
| UC-09 | تسجيل اهتمام استثماري | مستخدم |
| UC-10 | نشر آلي على وسائل التواصل | مسؤول |
| UC-11 | إدارة المستخدمين والاشتراكات والإصدارات | مسؤول |
| UC-12 | حذف الحساب وتصدير البيانات | مستخدم |

---

## 4. المتطلبات الوظيفية (Funktionale Anforderungen)

**الصيغة:** `/F-<مجال>-<رقم>/` ← الوصف ← **الوصول** ← **نقاط النهاية** ← الأولوية ← الحالة ← **الدليل**. الوصول: ز=زائر، م=مسجَّل، [دور]=دور معيّن.

### 4.1 المصادقة والحسابات (AUTH)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-AUTH-01/ | تسجيل بالبريد وكلمة مرور مع إرسال رابط تأكيد | ز | `POST /api/auth/register`, `/api/auth/email/verify`, `/email/resend-confirmation` | M | ✅ | `AuthController`، `RegisterCommand` |
| /F-AUTH-02/ | تسجيل الدخول بالبريد مع رد موحّد (بلا كشف وجود الحساب) | ز | `POST /api/auth/login` | M | ✅ | #243، ADR 2.1 |
| /F-AUTH-03/ | تسجيل/دخول بالهاتف: OTP ثم كلمة مرور، ورموز لكل غرض | ز | `POST /api/auth/phone/registration/send-otp|verify`, `/phone/login` | M | ✅ | `PhonePasswordAuthController`، 239 اختبار Auth |
| /F-AUTH-04/ | اختيار قناة OTP (SMS/Telegram/WhatsApp) مع إتاحة بحسب الدولة؛ الرمز يُقبل لقناته فقط | ز | `POST /api/auth/phone/channels` + `channel` في كل send/verify | S | ✅ / ❓ | ADR-014؛ تيليغرام مُجرَّب على أرقام المالك، واتساب لم يُجرَّب |
| /F-AUTH-05/ | دخول اجتماعي Google وApple (nonce) | ز | `POST /api/auth/social/google|apple` | S | ✅ | `SocialAuthenticationOrchestrator`، B-16 |
| /F-AUTH-06/ | استعادة كلمة المرور بالبريد وبالهاتف | ز | `POST /api/auth/forgot-password`, `/reset-password`, `/phone/password-reset/*` | M | ✅ | |
| /F-AUTH-07/ | تجديد الجلسة برمز تحديث دوّار في كوكي HttpOnly مع كشف إعادة الاستخدام | م | `POST /api/auth/refresh` | M | ✅ | ADR-002، ADR-015 |
| /F-AUTH-08/ | تسجيل الخروج وإبطال الرموز | م | `POST /api/auth/logout` | M | ✅ | #173 |
| /F-AUTH-09/ | إضافة بريد لحساب هاتفي وتأكيده | م | `POST /api/auth/email/add` | S | ✅ | `PhoneAuthController` |
| /F-AUTH-10/ | إعادة تحقق الهاتف كل 180 يومًا + 3 أيام مهلة | م | `GET/POST /api/auth/phone/reverification/*` | S | 🟡 | الإنفاذ معطّل افتراضيًا (`EnforcementEnabled=false`) |
| /F-AUTH-11/ | تغيير رقم الهاتف (OTP للجديد + كلمة المرور) | م | `POST /api/auth/phone/change/send-otp|verify` | S | ✅ | |
| /F-AUTH-12/ | سياسة كلمة المرور: قواعد + HIBP بقاطع دارة، على التسجيل والتغيير والاستعادة | ز/م | ضمني | M | ✅ | #156، `PasswordSecurityService` |
| /F-AUTH-13/ | تسجيل موافقة شروط الخدمة والخصوصية عند التسجيل وسحبها | م | `POST/GET /api/Users/me/consents`, `DELETE /me/consents/{policyType}` | M | ✅ | #252 |
| /F-AUTH-14/ | تغيير كلمة المرور | م | `POST /api/Users/me/change-password` | M | ✅ | |
| /F-AUTH-15/ | قفل مؤقت بعد محاولات فاشلة، حظر الحسابات المحظورة | ز | ضمني | M | ✅ | #205 |

### 4.2 الحساب والملف الشخصي (USR)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-USR-01/ | عرض الملف وتحديثه وتفعيل صفة وسيط | م | `GET/PUT /api/Users/me` | M | ✅ | |
| /F-USR-02/ | صورة الحساب (PNG/JPG/WebP ≤ 5MB) | م | `POST /api/Users/me/avatar` | S | ✅ | |
| /F-USR-03/ | ملف عام للمستخدم وتقييماته | ز | `GET /api/Users/{id}/profile`, `/ratings` | S | ✅ | |
| /F-USR-04/ | تقييم المالك بمحورين اختياريين بعد المراسلة | م | `GET/POST /api/Users/{id}/ratings[/eligibility]` | S | ✅ | #224، ترحيلة `AddUserRatingOptionalCriteria` |
| /F-USR-05/ | اختيار خطة الاشتراك | م | `POST /api/Users/me/plan` | M | ✅ | |
| /F-USR-06/ | طلب حذف الحساب بنافذة تأجيل **30 يومًا** وإعادة مصادقة | م | `DELETE /api/Users/me` | M | ✅ خلفية / 🟡 واجهة | `RequestDeleteUserCommandHandler`؛ نص الواجهة «فورًا» لا يطابق التأجيل |
| /F-USR-07/ | إلغاء طلب الحذف خلال النافذة | م | `POST /api/Users/me/deletion/cancel` | M | ✅ خلفية / ⛔ واجهة | لا شاشة في الواجهة |
| /F-USR-08/ | تصدير بيانات المستخدم | م | `GET /api/Users/me/export` (5/24h) | M | ✅ خلفية / ⛔ واجهة | |
| /F-USR-09/ | تنفيذ الحذف النهائي بمهمة خلفية ساعية | نظام | `AccountDeletionSweepHostedService` | M | ✅ | |
| /F-USR-10/ | سياسة بقاء إعلانات المحذوف (إخفاء الاسم فقط) | نظام | — | M | 🟡 | قرار أعمال مفتوح: لا إلغاء نشر تلقائي (`ACCOUNT-DELETION-PRODUCTION-READINESS.md`) |

### 4.3 العقارات والإعلانات (PRP)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-PRP-01/ | إنشاء عقار (بيع/إيجار/كلاهما) بنموذج ديناميكي حسب النوع | م + أهلية | `POST /api/Properties` | M | ✅ | `CreatePropertyCommandHandler` |
| /F-PRP-02/ | بوابة الأهلية: تأكيد بريد/هاتف + اختيار خطة + الحصة | م | في المعالج + `ListingEligibilityGuard` | M | ✅ | ADR 4.1، `IListingQuotaPolicy` |
| /F-PRP-03/ | الحصة تُفرَض في الخادم بقفل استشاري داخل معاملة؛ الإقامة القصيرة تشاركها | م | ضمني | M | ✅ | `ListingQuotaLock`، `ActiveListingCounter` |
| /F-PRP-04/ | تعديل وحذف العقار للمالك فقط (من التوكن لا الجسم) | م | `PUT/DELETE /api/Properties/{id}` | M | ✅ | `IPropertyOwnershipService` |
| /F-PRP-05/ | نشر/إلغاء نشر | م | `POST|PATCH /api/Properties/{id}/publish` | M | ✅ | |
| /F-PRP-06/ | دورة الصلاحية: 30 يومًا، تحذير قبل 5، مهلة 30 يومًا ثم حذف | نظام | `ListingExpiryHostedService` | M | ✅ | `ListingLifecyclePolicy` |
| /F-PRP-07/ | تمديد برسم 1$ بطلب → تأكيد إداري لوصول التحويل | م / Admin | `POST /api/Properties/{id}/extension`, `/extension/{transactionId}/confirm` | M | ✅ | |
| /F-PRP-08/ | إعلان مميّز 30 يومًا بـ 5$ بنفس آلية الدفع | م / Admin | `POST /{id}/featured`, `/featured/{transactionId}/confirm` | S | ✅ | |
| /F-PRP-09/ | تأكيد التوفّر من المالك | م | `PATCH /api/Properties/{id}/confirm-availability` | S | ✅ | شريط «قديم» بعد 30 يومًا |
| /F-PRP-10/ | عقارات المستخدم وصفحة الإدارة | م | `GET /api/Properties/mine`, `/{id}/manage` | M | ✅ | |
| /F-PRP-11/ | كشف التكرار | م | `GET /api/Properties/check-duplicate` | K | 🟡 | الخلفية فقط؛ لا استهلاك له في الواجهة |
| /F-PRP-12/ | قوائم مرجعية (محافظة/منطقة/حي/نوع/مرافق) وتحقق اتساق المعرّفات | ز | `GET /api/lookups/*`, `/api/amenities`, `/api/enums` | M | ✅ | #212 |
| /F-PRP-13/ | اقتراح مواقع جديدة وموافقة المسؤول | م / Admin | `/api/admin/location-suggestions` | K | ✅ | |
| /F-PRP-14/ | **الصور:** رفع (≤10/طلب، ≤20/عقار، ≤5MB، JPEG/PNG/WebP بتوقيع ثنائي)، رئيسية، حذف، تنظيف عند الفشل | م | `POST/GET/PATCH/DELETE /api/properties/{id}/images…` | M | ✅ | `UploadPropertyImagesCommandHandler` |
| /F-PRP-15/ | عرض قائمة وتفاصيل عامين مع Output Cache (30/60 ث) | ز | `GET /api/Properties`, `/{id}` | M | ✅ | `OutputCacheRegistration` |

### 4.4 البحث والاكتشاف (SRC)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-SRC-01/ | فلاتر (نوع عرض، موقع، سعر وعملة، غرف، مساحة، مرافق، مكتب…) وترتيب وصفحات | ز | `GET /api/Properties` | M | ✅ | `PropertyFilterDto` (28 مفتاح استعلام) |
| /F-SRC-02/ | بحث نصي (فهارس trigram) ويدعم الإعلانات القديمة بالاسم | ز | نفسه | M | ✅ | #212، #162 |
| /F-SRC-03/ | بحث جغرافي (القرب/النطاق) | ز | `GET /api/Properties/geo-search` | S | ✅ | PostGIS |
| /F-SRC-04/ | مفضّلات | م | `GET/POST/DELETE /api/Favorites` | S | ✅ | |
| /F-SRC-05/ | بحث محفوظ ومطابقة دورية (15 د) وإشعار | م | `/api/SavedSearch` | S | ✅ | `SavedSearchMatchHostedService` |
| /F-SRC-06/ | مشاركة وروابط Canonical ووسوم OG وتتبّع UTM والإسناد | ز | `POST /{id}/share-events`, `/attribution-events` | K | ✅ | حدود 20/40 في الدقيقة |
| /F-SRC-07/ | تحليلات عقار وسوق | م/ز | `GET /api/Analytics/property/{id}`, `/market` | K | ✅ | سياسة `market-insights` 5 د |

### 4.5 الزيارات والتقييمات والمراسلة والإشعارات (INT)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-INT-01/ | طلب زيارة: بعد ≥ ساعتين وخلال ≤ 90 يومًا؛ المالك لا يطلب زيارة لعقاره؛ 10/ساعة | م | `POST /api/Visits` | M | ✅ | `RequestVisitCommandValidator` |
| /F-INT-02/ | دورة الزيارة: تأكيد/رفض/اقتراح بديل/قبول-رفض البديل/إكمال/إلغاء | م | `PUT /api/Visits/{id}/confirm|decline|propose-alternate|accept-reschedule|decline-reschedule|complete`, `DELETE` | M | ✅ | |
| /F-INT-03/ | تقييم العقار يشترط زيارة مكتملة؛ 1–5 + تعليق ≤ 1000؛ 5/24h | م | `POST /api/Reviews` | M | ✅ | `AddReviewCommandHandler` |
| /F-INT-04/ | قراءة التقييمات وحذف تقييم المستخدم | ز/م | `GET /api/Reviews/property/{id}`, `DELETE /{id}` | M | ✅ | |
| /F-INT-05/ | مراسلة المالك، صندوق محادثات، تعليم مقروء (آخر 50 رسالة) | م | `GET/POST /api/Messages`, `/conversations`, `/conversations/read` | M | ✅ | #224 |
| /F-INT-06/ | إشعارات مخزَّنة + لحظية (SignalR) | م | `/api/Notifications`, `/notificationHub` | S | ✅ | |
| /F-INT-07/ | نموذج تواصل عام للإدارة بحد 5/ساعة | ز / Admin | `POST /api/Contact`, `GET/PUT/DELETE` للإدارة | M | ✅ | |

### 4.6 المكاتب العقارية (AGY)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-AGY-01/ | إنشاء/عرض/تعديل المكتب (slug ثابت) | م / مالك | `POST /api/Agencies`, `GET /me`, `PUT /{id}` | S | ✅ | |
| /F-AGY-02/ | دعوة الأعضاء وقبولها ورفضها (الحد 50) | مالك/م | `POST /{id}/invitations`, `GET /invitations/mine`, `POST /invitations/{id}/accept|decline` | S | ✅ خلفية / ⛔ واجهة | لا شاشة |
| /F-AGY-03/ | إزالة عضو / مغادرة | مالك/م | `DELETE /{id}/members/{memberUserId}` | S | ✅ خلفية / ⛔ واجهة | |
| /F-AGY-04/ | نقل الملكية، الشعار، التعطيل | مالك | `POST /{id}/transfer-ownership`, `/logo`, `DELETE /{id}` | K | ✅ خلفية / ⛔ واجهة | |
| /F-AGY-05/ | الصفحة العامة بالرابط القصير | ز | `GET /api/Agencies/{slug}` | S | ✅ خلفية / ⛔ واجهة | لا مسار واجهة |
| /F-AGY-06/ | الدور ليس كافيًا: المقارنة مع `AgencyId` الفعلي في كل معالج | م | ضمني | M | ✅ | ADR-006؛ لا عزل صفوف |

### 4.7 الإقامة القصيرة (SHS)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-SHS-01/ | إنشاء إعلان (نوع من قائمة 16، عملة ثابتة، موقع منظّم ودبوس اختياري، بوابة نشر) | م | `POST /api/short-stay/listings` | S | ✅ | `short-stay.md`، #220 |
| /F-SHS-02/ | تعديل/حذف/نشر/إلغاء نشر؛ لا حذف مع حجوزات جارية | م | `PUT/DELETE /{id}`, `PUT /{id}/publish|unpublish` | S | ✅ | #214 |
| /F-SHS-03/ | صور (≤10/طلب، 20/إعلان، 5MB) ومرافق | م | `POST /{id}/photos`, `PUT /{id}/amenities` | S | ✅ | |
| /F-SHS-04/ | أنواع غرف ووحدات وقواعد تسعير (عطلة>موسمي>نهاية أسبوع>يوم عمل>أساسي) وحد أدنى للإقامة | م | `POST /{id}/room-types`, `…/units`, `…/pricing-rules`, `…/minimum-stay-rules` | S | ✅ | |
| /F-SHS-05/ | بحث عام وتفاصيل وأنواع الإقامة | ز | `GET /api/short-stay/listings[/{id}|/accommodation-types]` | S | ✅ | |
| /F-SHS-06/ | التوفر ومعاينة السعر | ز | `GET /bookings/units/{unitId}/availability`, `/pricing-preview` | S | ✅ | |
| /F-SHS-07/ | الحجز ودورته: موافقة ← عربون ← تأكيد ← وصول ← مغادرة ← اكتمال؛ رفض/إلغاء/انتهاء/عدم حضور | م | `POST /api/short-stay/bookings`, `PUT /{id}/approve|reject|record-deposit-paid|confirm|check-in|check-out|complete`, `DELETE` | S | ✅ | `BookingStatus` (11 حالة) |
| /F-SHS-08/ | تقييم الإقامة | م | `POST /api/short-stay/reviews` | K | ✅ | |
| /F-SHS-09/ | حصة مشتركة مع العقارات وقفل استشاري | م | ضمني | M | ✅ | |

### 4.8 سوق الخدمات (SRV)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-SRV-01/ | كتالوج فئات وعروض خدمة (7 فئات) | ز | `GET /api/ServiceOfferings[/categories|/{id}]` | S | ✅ | |
| /F-SRV-02/ | طلب خدمة على عقار + ملاحظات ≤ 1000 + مستندات (≤5MB) | م | `POST /api/ServiceRequests`, `/{id}/documents` | S | ✅ | |
| /F-SRV-03/ | دورة الطلب: قبول/رفض/جدولة/بدء/إكمال/إلغاء + سجل تاريخ + تقييم | م/مزوّد | `PUT /{id}/accept|reject|schedule|start|complete|cancel`, `GET /{id}/history`, `POST /{id}/review` | S | ✅ | `ServiceRequestStatus` (9 حالات) |
| /F-SRV-04/ | ملف المزوّد وصندوقه | مزوّد | `GET/PUT /api/ServiceProviders/me`, `GET /ServiceRequests/provider-inbox` | S | ✅ | `ProviderGuard` |
| /F-SRV-05/ | إنشاء المزوّد واعتماده **بيد المسؤول فقط** (لا تسجيل ذاتي؛ الطلب عبر نموذج التواصل) | Admin | `POST /api/ServiceProviders`, `PUT /{id}/verification` | S | ✅ | |

### 4.9 التقييم العقاري (VAL)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-VAL-01/ | تقديم طلب تقييم (ضيف أو مسجَّل)؛ 10/ساعة | ز | `POST /api/ValuationInquiries` | S | ✅ | |
| /F-VAL-02/ | المسار السريع: ≥ 3 إعلانات مشابهة تُنتج تقديرًا فوريًا | ز | ضمني | S | ✅ | `MinimumComparablesForFastPath = 3` |
| /F-VAL-03/ | مسار المكاتب: مطابقة تدريجية (حي ← منطقة ← محافظة ← محافظة مجاورة) لتغطية ≥ 3 مكاتب | نظام | `OfficeMatchingService` | S | ✅ | |
| /F-VAL-04/ | ردّ المكتب على الدعوة | م مكتب | `GET /api/ValuationOfficeInvitations/mine`, `POST /{id}/respond` | S | ✅ | |
| /F-VAL-05/ | SLA: تذكير عند 18 س وانتهاء عند 24 س، إعادة محاولة الإشعار، idempotency | نظام | `ValuationInquiryExpiryHostedService` (15 د) | S | ✅ | ADR-009 |
| /F-VAL-06/ | موافقة صريحة قبل مشاركة بيانات التواصل | ز | `POST /api/ValuationInquiries/{id}/consent` | S | ✅ | |
| /F-VAL-07/ | تحكم تزامن `xmin` بين الردود والمسح | نظام | — | M | ✅ | ADR-010، `Concurrency.Tests` |
| /F-VAL-08/ | لوحة إدارية: قائمة الطلبات، إحصاءات المكاتب، تعليم/إلغاء تعليم مكتب | Admin | `GET /api/Admin/valuation-inquiries`, `/valuation-offices/statistics`, `POST …/flag|unflag` | K | ✅ | |

### 4.10 الاستثمار العقاري (INV)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-INV-01/ | عرض المشاريع وماليتها ومخاطرها ومستنداتها وتحديثاتها | ز | `GET /api/investments/projects[/…]` | K | ✅ | |
| /F-INV-02/ | حاسبة عائد | ز | `POST /projects/{id}/calculator` | K | ✅ | |
| /F-INV-03/ | قائمة مراقبة | م | `GET/POST/DELETE …/watchlist` | K | ✅ | |
| /F-INV-04/ | **تعبير عن اهتمام** بلا مبلغ ولا التزام، ويُسحب | م | `GET/POST/DELETE …/interest` | K | ✅ | `InvestmentInterest` |
| /F-INV-05/ | دورة الإدارة: مسودة ← مراجعة ← موافقة/رفض ← جدولة ← نشر ← تعليق/إغلاق + ماليات ومخاطر ومستندات وتحديثات | Admin | `/api/admin/investments/projects/*` | K | ✅ | `InvestmentProjectStatus` (8 حالات) |
| /F-INV-06/ | عرض إخلاء المسؤولية (المخاطر ليست ضمانًا) | ز | واجهة | M | ✅ | `INVESTMENTS.RISK.DISCLAIMER` |

### 4.11 النشر الآلي على وسائل التواصل (SOC) — Admin فقط

| المعرّف | المتطلّب | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|:-:|:-:|---|
| /F-SOC-01/ | قنوات وحسابات؛ ربط/فصل بيانات اعتماد لا تُعاد في أي استجابة | `/api/social-distribution/channels|accounts|accounts/{id}/connect|disconnect` | K | ✅ | `SocialAccountDto.hasCredential` |
| /F-SOC-02/ | قواعد توزيع: الأخص ثم الأعلى أولوية، تحقق، تفعيل/تعطيل | `…/rules*` | K | ✅ | |
| /F-SOC-03/ | زناد عند نشر العقار + Reconciliation (3 أيام) + بوابة أهلية (≥1 صورة، وصف ≥ 20) | `PropertyPublishedEvent` | K | ✅ | #221 |
| /F-SOC-04/ | منشورات: إنشاء/طابور/نشر/إعادة جدولة/إعادة محاولة/إعادة نشر/إلغاء + اعتماد المحتوى | `…/publications*` | K | ✅ | |
| /F-SOC-05/ | عامل إرسال (2 د) بـ Lease وAdvisory Lock؛ عالق → `AmbiguousOutcome` لا يُعاد تلقائيًا | `SocialPublicationDispatchHostedService` | K | ✅ | ADR-012 |
| /F-SOC-06/ | ناشر تيليغرام (Bot API) حقيقي | `TelegramBotPublisher` | K | ✅ / ❓ | لم يُختبر على قناة عبر النظام |
| /F-SOC-07/ | ناشر فيسبوك (Graph API) حقيقي | `FacebookGraphApiPublisher` | K | ✅ / ❓ | لم يُختبر على Page حقيقية |
| /F-SOC-08/ | ناشر إنستغرام (حاوية ثم نشر؛ JPEG فقط) | `InstagramGraphApiPublisher` | K | ✅ / ❓ | لم يُختبر |
| /F-SOC-09/ | TikTok/YouTube/LinkedIn | Placeholder | K | ⛔ | بالتصميم؛ `isLive=false` |
| /F-SOC-10/ | Dead Letters: عرض/حلّ/إعادة للطابور | `…/dead-letters*` | K | ✅ | |
| /F-SOC-11/ | مفتاح إيقاف شامل + إيقاف قناة + `Expired` عند رفض التوكن | `SocialDistribution__Enabled`, `…/channels/{id}/deactivate` | K | ✅ | #258 |
| /F-SOC-12/ | مقاييس وتنبيهان (توكن مرفوض / فشل متكرر) و`GET /publishers` مع `isLive` | `…/publishers`, Prometheus | K | ✅ | #260، #261 |
| /F-SOC-13/ | لا توكن في السجلات ولا في URL (فيسبوك/إنستغرام في جسم POST) | — | M | ✅ | #256، `SocialPublisherHttpLoggingTests` |
| /F-SOC-14/ | محتوى نصي حتمي + مدقّق حقائق يرفض الأسعار/الروابط المختلقة | `TemplateSocialContentGenerator` | K | ✅ | ADR-011 |
| /F-SOC-15/ | صور بعلامة تجارية مرفقة بالمنشور | — | K | ⛔ | الإرفاق معطّل (`AttachGeneratedAssetToAutomaticPublications=false`) |
| /F-SOC-16/ | تتبّع انتهاء توكن Meta وتجديده | — | K | ⛔ | |
| /F-SOC-17/ | تطبيق ترحيل `AddSocialPublicationLease` على قاعدة Production | — | M | ⏳ | **مطلوب من المالك** وإلا فشل العامل |

### 4.12 التسويق ما قبل الإطلاق (MKT)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة |
|---|---|---|---|:-:|:-:|
| /F-MKT-01/ | عملاء محتملون (5/ساعة) واستبيان (5/ساعة) وأحداث (60/د) | ز | `POST /api/leads`, `/api/surveys/landing`, `/api/marketing-events` | K | ✅ |
| /F-MKT-02/ | عروض نشطة (عام) وإدارتها | ز/Admin | `/api/offers` | K | ✅ |
| /F-MKT-03/ | لوحات الإدارة (قوائم وإحصاءات) | Admin | `GET /api/leads|surveys|surveys/stats|marketing-events/summary` | K | ✅ |

### 4.13 خطط الاشتراك والإدارة (ADM)

| المعرّف | المتطلّب | الوصول | نقاط النهاية | أ | الحالة | الدليل |
|---|---|---|---|:-:|:-:|---|
| /F-ADM-01/ | أربع خطط (مجانية 1 إعلان، أساسية 49$ → 50، بريميوم 99$ → 250، نخبة تواصل → غير محدود) | ز | `GET /api/Plans` | M | ✅ | `PlansSeed` |
| /F-ADM-02/ | إدارة المستخدمين والأدوار (عرض/دور/تعطيل/حذف) | Admin | `/api/Admin/users*`, `/roles` | M | ✅ | |
| /F-ADM-03/ | إدارة الاشتراكات (تفعيل/تمديد/إلغاء) وإبراز/تمديد العقارات يدويًا (1–730 يومًا) | Admin | `POST /api/Admin/users/{id}/subscription/*`, `/properties/{id}/feature|unfeature|extend` | M | ✅ | |
| /F-ADM-04/ | **بوابة دفع إلكتروني** للاشتراكات | — | — | S | ⛔ | القرار /A01/ |
| /F-ADM-05/ | إدارة تحديثات التطبيق: فحص عام (Android/IOS/Web) والإلزام مشتق من الحد الأدنى المدعوم؛ CRUD إداري + تفعيل/تعطيل + حذف ناعم | ز / Admin | `GET /api/app-updates/check`, `/api/admin/app-releases*` | S | ✅ | `app-update-management.md` |
| /F-ADM-06/ | إصدار البيئة والنسخة | Admin / سرّي | `GET /api/operational/version`, `/build-info` | S | ✅ | |
| /F-ADM-07/ | فحوص الصحة (`/health/ready`: PostGIS + Redis) | ز | `MapOperationalHealthEndpoints` | M | ✅ | |

---

## 5. متطلبات البيانات (Datenanforderungen)

### 5.1 النموذج
قاعدة PostgreSQL بامتداد PostGIS؛ `AppDbContext` يعرّف **74 `DbSet`**، **64 ترحيلة**. الترحيلات **إضافية فقط** (لا downgrade) وتُطبَّق يدويًا/عبر `HudhudNestApi.Migrator` لا عند الإقلاع.

### 5.2 الكيانات الرئيسية بحسب السياق
| السياق | كيانات رئيسية |
|---|---|
| الهوية | `UserAccount` (+ ASP.NET Identity)، `RefreshToken`، `PhoneOtpChallenge` (+ `OtpChannel`)، `ConsentRecord`، `AuditLog` |
| العقارات | `Property`، `PropertyImage`، `PropertyPriceHistory`، `PropertyShareEvent`، `PropertyAttributionEvent`، `Amenity`، `Transaction` (تمديد/إبراز) |
| المواقع | `Governorate`، `District`، `Neighborhood`، `PropertyType`، `Currency`، `LocationSuggestion` |
| التفاعل | `VisitRequest`، `PropertyReview`، `Message`، `Notification`، `Favorite`، `SavedSearch`، `UserRating` |
| المكاتب | `Agency`، `AgencyInvitation` |
| الإقامة القصيرة | `ShortStayListing`، `ShortStayListingPhoto`، `RoomType`، `AccommodationUnit`، `PricingRule`، `MinimumStayRule`، `UnitBookingRange`، `Booking`، `ShortStayReview`، `AccommodationType`، `HostVerificationRecord` |
| الخدمات | `ServiceProvider`، `ServiceOffering`، `ServiceRequest`، `ServiceRequestStatusHistory`، `ServiceRequestDocument`، `ServiceReview` |
| التقييم | `ValuationInquiry`، `ValuationOfficeInvitation`، `ValuationOfficeResponse`، `ValuationContactConsent` |
| الاستثمار | `InvestmentProject`، `InvestmentProjectFinancials`، `InvestmentRiskAssessment`، `InvestmentDocument`، `InvestmentUpdate`، `InvestmentWatchlistItem`، `InvestmentInterest` |
| النشر الاجتماعي | `SocialChannel`، `SocialAccount` (+ بيانات اعتماد مشفَّرة)، `DistributionRule`، `DistributionRun`، `SocialPublication`، `SocialPostContent`، `SocialPublicationStatusHistory`، `SocialPublicationDeadLetter`، `SocialMediaAsset` |
| التسويق | `ContactMessage`، `Lead`، `Offer`، `SurveyResponse`، `MarketingEvent` |
| الخطط والتحديثات | `Plan`، `AppRelease` |

### 5.3 قواعد البيانات الهامة
- **تزامن:** عمود `xmin` على `Property` و`Transaction` و`UserAccount` و`ValuationInquiry` و`ValuationOfficeInvitation`.
- **حذف ناعم:** `AppRelease`، حسابات المستخدمين (إخفاء PII).
- **فريدة:** رقم الهاتف المطبَّع (فهرس مرشَّح)، بريد الحساب، `Version` لكل منصّة بين الإصدارات المفعّلة.
- **الاحتفاظ:** سجلّ التدقيق — **معطّل افتراضيًا** (`AuditLogRetention:Enabled=false`)؛ مدة الاحتفاظ بعد الحذف **تتطلب قرارًا قانونيًا** (`docs/privacy/data-inventory.md`).
- **الأسرار:** لا تُخزَّن بياناتها مكشوفة؛ بيانات اعتماد حسابات النشر مشفَّرة ولا تُعاد.

### 5.4 حذف الحساب (/F-USR-06…10/)
طلب → جدولة بنافذة 30 يومًا (قابلة للإلغاء) → مهمة ساعية تنفّذ إخفاء/حذف PII وإبطال الجلسات؛ تبقى الإعلانات والرسائل والتقييمات باسم «مستخدم محذوف».

---

## 6. متطلبات الأداء والسعة (Leistungsanforderungen)

### 6.1 أهداف مستوى الخدمة (SLO)
| المعرّف | الهدف | الحالة |
|---|---|---|
| /L01/ | التوفر: غير-5xx ≥ **99.5%** (30 يومًا) | ✅ قواعد Prometheus + تنبيهات Fast/Slow-Burn |
| /L02/ | الاستجابة: ≥ **95%** من الطلبات ≤ **500ms** | ✅ هستوغرامات؛ ميزانيات الأداء معتمدة (#161) ومراجعة SRE معلَّقة |
| /L03/ | المصادقة: ≥ **99.0%** نجاحًا للمحاولات الصالحة | ✅ |

### 6.2 حدود المعدل (من `RateLimitingRegistration.cs`)
| السياسة | الحد/النافذة |
|---|---|
| `send-otp` | 3 / 15 د |
| `verify-otp` | 5 / 15 د |
| `auth-password-reset` | 3 / 60 د |
| `auth-login` | 10 / 1 د |
| `auth-register` | 5 / 10 د |
| `auth-refresh`، `auth-logout` | 20 / 5 د |
| `contact`، `leads-submit`، `surveys-submit` | 5 / 60 د |
| `marketing-events` | 60 / 1 د |
| `visits` | 10 / 60 د |
| `reviews` | 5 / 24 س |
| `agencies-public`، `public-read`، `public-search`، `shortstay-search` | 120 / 1 د |
| `geo-search` | 60 / 1 د |
| `property-share-events` | 20 / 1 د |
| `property-attribution-events` | 40 / 1 د |
| `social-distribution-write` | 30 / 1 د |
| `shortstay-booking`، `service-requests`، `valuation-inquiries` | 10 / 60 د |
| `service-request-documents` | 20 / 60 د |
| `account-delete` | 3 / 60 د |
| `data-export` | 5 / 24 س |

يعمل المحدِّد على Redis في Production (يفشل **مغلقًا** بـ 503 إن غاب)، وفي الذاكرة خلاف ذلك.

### 6.3 السعة والتخزين المؤقت
- Output Cache: 30 ث (قوائم)، 60 ث (تفاصيل)، 5 د (تحليلات السوق)، الحد 2MB للجسم و128MB للمجموع.
- ضغط Brotli/Gzip على HTTPS.
- حدود الملفات: 5MB/صورة؛ 10 صور/طلب؛ 20 صورة/عقار أو إعلان إقامة.
- بلا حالة على مستوى العملية؛ SignalR backplane عبر Redis للتوسّع الأفقي.

---

## 7. متطلبات الجودة (Qualitätsanforderungen)

| المعرّف | الصفة | المتطلّب القابل للقياس | الحالة |
|---|---|---|---|
| /Q01/ | الأمان — المصادقة | Access 30 د، Refresh 30 يومًا في كوكي `HttpOnly; Secure; SameSite=None; Partitioned`، كشف إعادة الاستخدام | ✅ |
| /Q02/ | الأمان — CSRF | توكن مزدوج في جسم JSON؛ طلبات Bearer مستثناة من توكن الاسم المجهول؛ `logout` لا يفشل | ✅ |
| /Q03/ | الأمان — رؤوس | CSP/X-Frame-Options، HSTS 365 يومًا + Preload في Production **وStaging** | ✅ |
| /Q04/ | الأمان — عدم التسريب | استجابات موحّدة لتسجيل الدخول ورموز OTP؛ لا توكن في سجلات | ✅ |
| /Q05/ | الأمان — ملفات | توقيع ثنائي لا امتداد فقط | ✅ |
| /Q06/ | الأمان — أسرار | تدوير `Jwt__Key` و`OtpSettings__SecretKey` في Production بقيمة ≥ 64 حرفًا عشوائيًا | ⏳ **مفتوح (R12)** |
| /Q07/ | الموثوقية — RPO | ≤ 60 دقيقة (تصميم) | 🟡 النسخ الاحتياطي **يومي** (#236) — يجب التوفيق |
| /Q08/ | الموثوقية — RTO | ≤ 120 دقيقة، مُقاس بسير عمل استرجاع آلي (`restore-drill-evidence.json`) | ✅ |
| /Q09/ | الموثوقية — Redis | HA بـ Sentinel مُختبَر في CI | ✅ (CI) / ❓ (Production) |
| /Q10/ | الصيانة | فصل طبقات مفروض باختبارات معمارية (133) كبوابة CI | ✅ |
| /Q11/ | الاختبار | 9 مشاريع اختبار (~2115 تصريح اختبار) + دخان على Staging | ✅ |
| /Q12/ | الملاحظة | OTel (traces/metrics/logs) → Prometheus/Grafana/Tempo؛ أسماء `hudhudnest_*` | ✅ |
| /Q13/ | قابلية النقل | صورة Docker `chiseled` بلا shell، مستخدم غير جذري | ✅ |
| /Q14/ | قابلية الاستخدام | RTL عربي كامل + en/de؛ وضع داكن؛ i18n بتكافؤ المفاتيح | ✅ (فحص `check:i18n`) |
| /Q15/ | التوافر الإقليمي لـ OTP | تسليم موثوق إلى أرقام سوريا | 🟡 تيليغرام ✅ (أرقام المالك) / SMS غير محلول / WhatsApp غير مدعوم (R15) |
| /Q16/ | التوسّع | `BackgroundJobLock` لكل عامل خلفي، أقفال استشارية للحصص وحد OTP | ✅ |

---

## 8. واجهة المستخدم (Benutzeroberfläche)

### 8.1 المسارات الرئيسية وحمايتها (`app.routes.ts`)
| المجموعة | المسارات | الحماية |
|---|---|---|
| عامة | `/home`، `/landing`، `/properties`، `/properties/:id`، `/short-stay`، `/short-stay/:id`، `/investments`، `/investments/:id`، `/valuation`، `/pricing`، `/kontakt`، `/reviews`، `/users/:id`، `/privacy-policy`، `/terms-of-service`، `/account-deletion`، `/offline`، `/not-found`، `/not-authorized` | — |
| المصادقة | `/login`، `/auth/login|register|forgot-password|reset-password|verify-email|phone-register|phone-password-reset`، `/language` | — |
| مسجَّل | `/my-listings`، `/my-agency`، `/favorites`، `/saved-searches`، `/notifications`، `/visits`، `/messages[/:propertyId]`، `/my-services`، `/analytics`، `/profile`، `/profile/phone-reverify|phone-change`، `/checkout/:kind/:propertyId`، `/investments/watchlist`، `/short-stay/bookings|manage[/new|/:id]` | `AuthGuard` |
| إنشاء إعلان | `/property-form` | `AuthGuard` + `ListingEligibilityGuard` |
| تعديل إعلان | `/properties/edit/:id` | `AuthGuard` |
| مزوّد خدمة | `/provider/requests[/:id]`، `/provider/profile` | `ProviderGuard` |
| مسؤول | `/admin/messages`، `/admin/users[/:id[/properties]]`، `/admin/location-suggestions`، `/admin/investments[/new|/:id]`، `/admin/marketing`، `/admin/app-updates`، `/admin/social-distribution` | `AdminGuard` (ملاحظة: **التفويض الحقيقي في الخادم**؛ الحارس تجربة استخدام) |

### 8.2 المتطلبات
| المعرّف | المتطلّب | أ | الحالة |
|---|---|:-:|:-:|
| /UI-01/ | RTL كامل للعربية، اتجاه LTR للإنجليزية والألمانية، العربية افتراضية | M | ✅ |
| /UI-02/ | وضع داكن/فاتح | S | ✅ |
| /UI-03/ | تصميم متجاوب للجوال + Capacitor | M | ✅ |
| /UI-04/ | شارة بيئة (🟠 STAGING للجميع، 🟢 PRODUCTION للمسؤول) | S | ✅ |
| /UI-05/ | نافذة تحديث التطبيق (اختياري/إلزامي) | S | ✅ |
| /UI-06/ | شاشات لإدارة المكتب (دعوات/أعضاء/شعار) والصفحة العامة للمكتب | S | ⛔ |
| /UI-07/ | شاشة إلغاء الحذف وتصدير البيانات + تصحيح نص «فورًا» في نافذة الحذف | M | ⛔ |
| /UI-08/ | حقل تغيير حالة العقار (محجوز/مباع/مؤجَّر) في الواجهة | S | ❓ |

---

## 9. المتطلبات القانونية والامتثال (Rechtliche Anforderungen)

| المعرّف | المتطلّب | الحالة |
|---|---|---|
| /R01/ | تسجيل موافقة شروط الخدمة وسياسة الخصوصية وسحبها | ✅ |
| /R02/ | صفحة عامة لحذف الحساب لمتاجر التطبيقات `/account-deletion` | ✅ (رابط الإفصاح يحتاج النطاق الرسمي في المتاجر) |
| /R03/ | تصدير البيانات (حق الاطلاع) | 🟡 خلفية فقط |
| /R04/ | قناة رسمية لطلبات الخصوصية | 🟡 `privacy@` و`support@hudhudnest.com` موجودان؛ لا نظام تذاكر ولا SLA |
| /R05/ | مدة احتفاظ سجلات التدقيق | ⏳ قرار قانوني |
| /R06/ | Apple Developer Program المدفوع للدخول عبر Apple على iOS | ⏳ [مالك] |

---

## 10. البيئة التقنية (Technische Produktumgebung)

| الطبقة | التقنية |
|---|---|
| الخلفية | .NET 8 / ASP.NET Core، MediatR، FluentValidation، EF Core + Npgsql + NetTopologySuite، Identity |
| قاعدة البيانات | PostgreSQL + PostGIS (Render؛ خطة الانتقال إلى Supabase) |
| ذاكرة مؤقتة | Redis (Upstash في Production، Valkey على Render في Staging) |
| الوسائط | Cloudinary (جذر `staging/` أو `hudhudnest/` بحسب البيئة) |
| البريد | Resend (SMTP احتياطي؛ Console مرفوض في Production) |
| SMS/OTP | Twilio، D7 Networks، Unimatrix، HTTP عام؛ Telegram Gateway؛ WhatsApp Cloud (معطّل افتراضيًا) |
| الدخول الاجتماعي | Google وApple |
| النشر الاجتماعي | Telegram Bot API، Meta Graph API |
| الواجهة | Angular 20 (Standalone)، Capacitor (Android/iOS)، Netlify (+ Edge Functions للمعاينات الاجتماعية وخريطة الموقع) |
| المراقبة | OpenTelemetry، Prometheus، Grafana، Tempo |
| الاستضافة | Render (API)، Netlify (واجهة)، نطاقات `hudhudnest.com`، `api.hudhudnest.com`، `staging-api.hudhudnest.com` |
| الحاويات | Docker متعدد المراحل (`aspnet:8.0-jammy-chiseled-extra`) |

---

## 11. بيئة التطوير والتشغيل (Entwicklungsumgebung)

| المعرّف | المتطلّب | الحالة |
|---|---|---|
| /E01/ | حل واحد بأربعة مشاريع + 9 اختبار + 3 أدوات؛ إدارة حزم مركزية وأقفال استعادة (`--locked-mode`) | ✅ |
| /E02/ | 9 سير عمل: `ci`، `production-gate`، `database-backup`، `database-restore-drill`، `observability-validation`، `performance-validation`، `redis-ha-failover`، `rollback-production`، `supply-chain-validation` (مشغّلات `ubuntu-24.04`) | ✅ |
| /E03/ | بوابة إصدار: بناء + اختبارات + أداء + `staging-smoke` + استرجاع قاعدة + `production-deployment-gate` | ✅ |
| /E04/ | Staging يُنشَر تلقائيًا بعد الدمج؛ Production **يدوي فقط** (`workflow_dispatch` من `master`) | ✅ |
| /E05/ | حارسا إقلاع وعلامات `Staging__*` تمنع تداخل البيئتين | ✅ |
| /E06/ | Rollback مؤتمت بإذن وسجل حادثة | ✅ |
| /E07/ | حماية فرع `master` وReviewers للبيئات | ⛔ (قيد الخطة المجانية) |
| /E08/ | التحقق من إيقاف Auto-Deploy لخدمة Production في Render | ❓ |
| /E09/ | ترقية إلى .NET 10 | ⏳ (`net10-migration-plan.md`) |
| /E10/ | تحويل الواجهة إلى النطاقين المخصّصين `api.hudhudnest.com` و`staging-api.hudhudnest.com` | ⏳ [مالك] |

---

## 12. التقسيم إلى منتجات جزئية (Teilprodukte) ومراحل التسليم

| المنتج الجزئي | يضم | الحالة |
|---|---|---|
| **TP-1 النواة** | AUTH، USR، PRP، SRC، INT، ADM-01..03 | ✅ مُسلَّم |
| **TP-2 الأعمال الموسّعة** | AGY، SHS، SRV، VAL | ✅ خلفية، 🟡 واجهة المكاتب |
| **TP-3 الفرص والنمو** | INV، MKT، ADM-05 | ✅ |
| **TP-4 التوزيع الاجتماعي** | SOC | ✅ برمجيًا / ❓ حقيقيًا |
| **TP-5 التشغيل والجودة** | E01–E10، Q01–Q16 | 🟡 بنود مالك مفتوحة |
| **المرحلة التالية المقترحة** | (1) تدوير الأسرار /Q06/ ← (2) ترحيل Lease /F-SOC-17/ ← (3) أول نشر حقيقي تيليغرام ← (4) حسم SMS لسوريا /Q15/ ← (5) شاشات المكتب والحذف /UI-06/07/ ← (6) توفيق RPO مع النسخ اليومي /Q07/ | ⏳ |

---

## 13. اختبارات القبول (Abnahmekriterien)

| المعرّف | السيناريو (المُدخل ← المتوقَّع) | يغطي |
|---|---|---|
| AT-AUTH-1 | تسجيل بالبريد ← رسالة تأكيد؛ الدخول قبل التأكيد حسب السياسة؛ رد دخول فاشل موحّد لحساب موجود وغير موجود | /F-AUTH-01,02/ |
| AT-AUTH-2 | OTP: 4 طلبات إرسال خلال 15 د ← الرابع 429؛ رمز لقناة أخرى ← `OTP_INVALID` بلا استهلاك محاولة؛ رمز بعد 5 د ← مرفوض | /F-AUTH-03,04/ |
| AT-AUTH-3 | استخدام Refresh Token قديم ← 401 وإبطال كل الرموز وتدوير SecurityStamp | /F-AUTH-07/ |
| AT-AUTH-4 | طلب POST بكوكي ودون `X-XSRF-TOKEN` ← 403؛ طلب Bearer ← يمر | /Q02/ |
| AT-PRP-1 | مستخدم بلا تأكيد تواصل ينشئ إعلانًا ← رفض؛ بلا خطة ← توجيه إلى `/pricing`؛ الخطة المجانية وإعلان ثانٍ ← 409 | /F-PRP-02,03/ |
| AT-PRP-2 | رفع ملف تنفيذي بامتداد `.png` ← رفض؛ 21 صورة ← رفض؛ 6MB ← رفض | /F-PRP-14/ |
| AT-PRP-3 | إعلان عمره 25 يومًا ← إشعار تحذير؛ بعد 30 ← يخرج من البحث؛ بعد 60 ← يُحذف؛ تمديد مؤكَّد ← مدة جديدة من يوم التأكيد | /F-PRP-06,07/ |
| AT-SRC-1 | طلبان يختلفان بـ `governorateId` فقط ← ردّان مختلفان (لا تصادم كاش) | /F-SRC-01/, #254 |
| AT-INT-1 | طلب زيارة بعد ساعة ← رفض؛ بعد 91 يومًا ← رفض؛ المالك لعقاره ← رفض | /F-INT-01/ |
| AT-INT-2 | تقييم بلا زيارة مكتملة ← رفض؛ تقييم بعد إكمال ← نجاح؛ السادس خلال 24 س ← 429 | /F-INT-03/ |
| AT-SHS-1 | حجز على تواريخ محجوزة ← تعارض؛ إعلان بلا دبوس/محافظة ← نشر مرفوض؛ حذف إعلان عليه حجز جارٍ ← مرفوض | /F-SHS-02,07/ |
| AT-VAL-1 | منطقة بها ≥ 3 إعلانات مشابهة ← تقدير فوري؛ وإلا ← دعوات لمكاتب؛ بعد 18 س ← تذكير؛ بعد 24 س ← انتهاء | /F-VAL-02..05/ |
| AT-VAL-2 | ردّ مكتب متزامن مع مسح الانتهاء ← لا فقدان تحديث (409 أو تجاوز صفّ خاسر) | /F-VAL-07/ |
| AT-INV-1 | تسجيل اهتمام ← لا مبلغ ولا حالة دفع؛ سحب الاهتمام ينجح | /F-INV-04/ |
| AT-SOC-1 | مفتاح الإيقاف مفعّل ← لا إنشاء ولا إرسال؛ منشور عالق بعد Lease ← `AmbiguousOutcome` ولا إعادة تلقائية | /F-SOC-05,11/ |
| AT-SOC-2 | التقاط كل سجلات HttpClient أثناء نشر ← لا توكن | /F-SOC-13/ |
| AT-SOC-3 | **(يدوي على بيئة حقيقية)** نشر عقار فعلي إلى قناة تيليغرام تجريبية ← يظهر المنشور ويُسجَّل `ExternalPostId` | /F-SOC-06/ ❓ |
| AT-USR-1 | طلب حذف حساب ← جدولة 30 يومًا؛ إلغاء داخل النافذة ← يعود الحساب؛ بعدها ← تنفّذه المهمة | /F-USR-06..09/ |
| AT-ADM-1 | فحص تحديث لإصدار أقدم من `MinimumSupportedVersion` ← `mandatory=true`؛ لإصدار أحدث من الحد وأقدم من الأحدث ← اختياري؛ بلا إصدار مفعّل ← «لا تحديث» لا 404 | /F-ADM-05/ |
| AT-ENV-1 | تشغيل Production بإعدادات قاعدة اسمها يحوي `staging` ← فشل الإقلاع | /E05/ |
| AT-DR-1 | سير عمل الاسترجاع الآلي ← يخرج `restore-drill-evidence.json` ضمن RTO | /Q08/ |

---

## 14. المخاطر والقيود المعروفة (مرتبطة بالمتطلبات)

من [ARD §7](../architecture/ARD/ARD-HudhudNestApi.md):

| الخطر | المتطلّب المتأثّر | الإجراء |
|---|---|---|
| R1 Redis مشترك | /L01/، /Q16/ | ADR-005 |
| R2 لا عزل صفوف للمكاتب | /F-AGY-06/ | ADR-006 |
| R9/R13/R14 النشر الاجتماعي غير مجرَّب حقيقيًا / نشر مزدوج / ترحيل Lease | /F-SOC-06..08،17/ | خطوات مالك |
| **R12 أسرار Production** | /Q06/ | تدوير |
| R15 OTP لسوريا | /Q15/، /F-AUTH-03,04/ | تيليغرام + حسم SMS |
| R16 حماية `master`/Auto-Deploy | /E07,08/ | تحقق يدوي |
| R17 إزالة قيم JWT القديمة | /F-AUTH-07/ | بعد ≥ 30 يومًا |
| R18 تحويل الواجهة للنطاقات المخصّصة | /E10/ | مالك |

**فجوات واجهة/خلفية مكتشفة أثناء إعداد هذه الوثيقة:** (أ) نص نافذة حذف الحساب «فورًا» لا يطابق التأجيل 30 يومًا، ولا شاشات لإلغاء الحذف/تصدير البيانات؛ (ب) لا شاشات لدعوات المكتب وأعضائه وشعاره وصفحته العامة؛ (ج) `check-duplicate` بلا مستهلك في الواجهة؛ (د) نصّا `docs/SUBSCRIPTION-PAGE-REPORT-AR.md` في مستودع الواجهة قديمان (يقولان «لا باك-إند للاشتراكات») بينما `PlansController` و`SelectPlan` موجودان.

---

## 15. مصفوفة التتبّع (Traceability) — مختصر

| الهدف | المتطلبات | نقاط نهاية | كيانات | اختبارات رئيسية |
|---|---|---|---|---|
| /M01/ | /F-AUTH-01..15/ | `Auth*`، `PhonePasswordAuth` | `UserAccount`، `RefreshToken`، `PhoneOtpChallenge` | `Auth.Tests`، `Integration.Tests/Auth` |
| /M02/ | /F-PRP-01..15/ | `Properties*` | `Property`، `PropertyImage`، `Transaction` | `Application.Tests/Listings`، `Concurrency.Tests` |
| /M03/ | /F-SRC-01..07/ | `Properties`، `SavedSearch` | `Property`، `SavedSearch` | `OutputCacheVaryByQueryTests` |
| /M04/ | /F-INT-01..07/ | `Visits`، `Reviews`، `Messages` | `VisitRequest`، `PropertyReview`، `Message` | `Application.Tests` |
| /M05/ | /F-ADM-01..03/، /F-PRP-03/ | `Plans`، `Users/me/plan` | `Plan` | `CreatePropertyCommandHandlerTests` |
| /M06/ | /Q01..Q06/ | middleware | — | `Architecture.Tests` (133) |
| /M07/ | /F-USR-06..10/، /R01..R04/ | `Users` | `UserAccount`، `ConsentRecord` | `Application.Tests/Users` |
| /M08/ | /UI-01..08/ | — | — | `check:i18n`، e2e الواجهة |
| /M09/ | /E01..E10/، /Q07..Q13/ | workflows | — | `Observability.Tests`، `StagingSmokeTests` |
| /S04/ | /F-VAL-01..08/ | `Valuation*` | `ValuationInquiry`… | `Concurrency.Tests` (A/B/C/D) |
| /K01/ | /F-SOC-01..17/ | `SocialDistribution` | `SocialPublication`… | `Infrastructure.Tests`، `Integration.Tests` |

---

## 16. ملحقات

### 16.1 مسرد (عربي / ألماني / إنجليزي)
| عربي | Deutsch | English |
|---|---|---|
| دفتر المتطلبات التفصيلية | Pflichtenheft | Detailed requirements specification |
| دفتر المتطلبات (العميل) | Lastenheft | Requirements specification (customer) |
| إلزامي / مرغوب / اختياري | Muss / Soll / Kann | Must / Should / Could |
| معايير الحدّ | Abgrenzungskriterien | Delimitation criteria |
| مجال الاستخدام | Produkteinsatz | Product use |
| نظرة عامة على المنتج | Produktübersicht | Product overview |
| متطلبات الأداء | Leistungsanforderungen | Performance requirements |
| متطلبات الجودة | Qualitätsanforderungen | Quality requirements |
| واجهة المستخدم | Benutzeroberfläche | User interface |
| منتج جزئي | Teilprodukt | Sub-product |
| اختبار القبول | Abnahmetest | Acceptance test |
| إمكانية التتبّع | Rückverfolgbarkeit | Traceability |
| نتيجة غامضة | Mehrdeutiges Ergebnis | Ambiguous outcome |

### 16.2 نقاط النهاية العامة (بلا مصادقة) — مختصر
`Properties` (GET)، `PropertyImages` (GET)، `Reviews/property`، `Users/{id}/profile|ratings`، `Agencies/{slug}`، `ShortStayListings` (GET)، `ShortStayBookings` (availability/pricing-preview)، `ServiceOfferings` (GET)، `ServiceProviders/{id}/reviews`، `Investments/projects*` (GET + calculator)، `Plans`، `Lookups`، `Amenities`، `Enums`، `Analytics/market`، `AppUpdates/check`، `Csrf`، `Auth` (register/login/…/social)، `PhoneAuth`/`PhonePasswordAuth` (غير المحمية)، `Contact` (POST)، `Leads`، `Surveys/landing`، `MarketingEvents` (POST)، `Offers/active`، `ValuationInquiries` (3 نقاط)، `Operational/build-info` (بسرّ، Staging)، `Properties/{id}/share-events|attribution-events`. القائمة المعتمدة مفروضة آليًا باختبار `PublicEndpointPolicyTests`.

### 16.3 المراجع
[ARD v2.0](../architecture/ARD/ARD-HudhudNestApi.md) · [دليل المستخدم](USER-GUIDE-AR.md) · `docs/social-distribution/*` · `docs/phone-password-authentication-and-reverification.md` · `docs/architecture/short-stay.md` · `docs/app-update-management.md` · `docs/operations/environments-and-release-flow.md` · `docs/privacy/data-inventory.md`.

---

*نهاية الوثيقة. أي تعارض بين هذا الدفتر والكود يُحسَم لصالح الكود ثم يُصحَّح الدفتر.*
