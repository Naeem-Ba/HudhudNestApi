using FluentValidation.TestHelper;
using Moq;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.CreateProperty;
using HudhudNestApi.Application.Listings.Validators;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Enums;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// اختبارات شاملة لـ CreatePropertyCommandValidator.
///
/// المنهجية:
///   - كل اختبار يختبر حالة واحدة فقط (Single Responsibility)
///   - ValidCommand() يُعيد بيانات صحيحة كنقطة انطلاق
///   - نستخدم "with" لتغيير حقل واحد فقط في كل اختبار
///   - الأسماء تصف السلوك: [الحالة]_[النتيجة]
///
/// تغطية القواعد:
///   ✅ OwnerId (NotEmpty)
///   ✅ Title (NotEmpty + MaxLength 200)
///   ✅ Description (NotEmpty + MaxLength 5000)
///   ✅ City (NotEmpty + MaxLength 150)
///   ✅ CountryCode (ISO 3166-1 alpha-2)
///   ✅ CurrencyCode (ISO 4217)
///   ✅ ListingType.ForRent  → ColdRent مطلوب
///   ✅ ListingType.ForSale  → PurchasePrice مطلوب
///   ✅ ListingType.ForRentAndSale → كلاهما مطلوب
///   ✅ AmenityIds (لا تكرار)
///   ✅ Latitude/Longitude (كلاهما أو لا شيء + نطاق)
///   ✅ Rooms (1-50)
///   ✅ Area (> 10)
/// </summary>
public sealed class CreatePropertyCommandValidatorTests
{
    // ══════════════════════════════════════════════════════════════════
    // الـ Validator المُختبَر — instance واحد يكفي (stateless)
    //
    // يحتاج المُدقِّق منذ إضافة قاعدة "الأرض تتطلب مساحة" إلى ICommonLookupService
    // (لمعرفة Category الخاصة بـ PropertyTypeId). Fake بسيط يكفي هنا بدل Mock
    // كامل: PropertyTypeId=1 → "Residential" (يطابق كل الاختبارات القائمة التي
    // تستخدم PropertyTypeId=1 دون تغيير سلوكها)، وPropertyTypeId=2 → "Land"
    // (تستخدمه اختبارات الأرض الجديدة أدناه).
    // ══════════════════════════════════════════════════════════════════
    private static readonly IReadOnlyList<PropertyTypeLookupDto> PropertyTypeCatalog = new List<PropertyTypeLookupDto>
    {
        new(1, "apartment", "شقة سكنية", "Apartment", "Residential", "apartment"),
        new(2, "residential-land", "أرض سكنية", "Residential Land", "Land", "land-residential")
    };

    private readonly Mock<ICommonLookupService> _lookupsMock = BuildLookupsMock();
    private readonly CreatePropertyCommandValidator _validator;

    public CreatePropertyCommandValidatorTests()
    {
        _validator = new CreatePropertyCommandValidator(_lookupsMock.Object);
    }

    private static Mock<ICommonLookupService> BuildLookupsMock()
    {
        var mock = new Mock<ICommonLookupService>();
        mock.Setup(x => x.GetPropertyTypeCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PropertyTypeCatalog);
        return mock;
    }

    // ══════════════════════════════════════════════════════════════════
    // 🏗️ بيانات صحيحة افتراضية — تُستخدَم كقاعدة في كل الاختبارات
    //
    // لماذا record مع positional parameters؟
    // CreatePropertyCommand هو "record" في C#، لذلك نستخدم
    // "with" لتعديل حقل واحد دون المساس بالباقي.
    // ══════════════════════════════════════════════════════════════════
    private static CreatePropertyCommand ValidCommand() => new(
        OwnerId: Guid.NewGuid(),
        Title: "شقة فاخرة في مركز برلين",
        Description: "شقة واسعة مطلة على الحديقة، 3 غرف نوم، تشطيب ممتاز",
        ListingType: ListingType.ForRent,
        Street: "Kurfürstendamm 100",
        City: "Berlin",
        Region: null,
        CountryCode: "DE",
        PostalCode: "10709",
        // Phase-0 follow-up: Governorate/District/PropertyType are now
        // required (see CreatePropertyCommandValidator), so the "valid
        // command" fixture must supply them — NeighborhoodId/NeighborhoodText
        // stay optional/null, matching real-world coverage gaps.
        GovernorateId: 1,
        DistrictId: 1,
        DistrictText: null,
        NeighborhoodId: null,
        NeighborhoodText: null,
        PropertyTypeId: 1,
        Latitude: null,
        Longitude: null,
        ColdRent: 1200m,
        WarmRent: 1450m,
        PurchasePrice: null,
        Deposit: 2400m,
        AdditionalCosts: null,
        CurrencyCode: "EUR",
        Rooms: 3,
        Area: 85m,
        Floor: 2,
        HasBalcony: true,
        HasElevator: false,
        HasParkingSpace: false,
        HeatingType: HeatingType.Central,
        AvailableFrom: DateTime.UtcNow.AddMonths(1),
        AmenityIds: null,
        // ListingType الافتراضي أعلاه هو ForRent، لذا يجب توفير تفاصيل الإيجار
        // (مطلوبة الآن لأي إعلان إيجار) حتى تبقى "الحالة الصحيحة الافتراضية"
        // صحيحة فعلاً بعد إضافة قاعدة مدة الإيجار.
        RentalStartDate: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
        RentalEndDate: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(7)),
        RentalDurationType: RentalDurationType.SixMonths
    // LegalStatus: null — غير مطلوب للإيجار؛ اختبارات البيع تحدده صراحة.
    // AreaUnit: يبقى الافتراضي SquareMeter (يطابق Area=85m² أعلاه).
    );

    // ══════════════════════════════════════════════════════════════════
    // 🟢 المجموعة 1: الحالات الصحيحة — يجب أن تمر بدون أخطاء
    // ══════════════════════════════════════════════════════════════════
    #region ✅ Valid Cases

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ValidCommand_AllFieldsCorrect_PassesWithNoErrors()
    {
        // Arrange
        var cmd = ValidCommand();

        // Act
        var result = await _validator.TestValidateAsync(cmd);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Country Codes الصحيحة ─────────────────────────────────────────
    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData("DE")]  // ألمانيا
    [InlineData("SY")]  // سوريا
    [InlineData("US")]  // الولايات المتحدة
    [InlineData("GB")]  // بريطانيا
    [InlineData("AE")]  // الإمارات
    [InlineData("SA")]  // السعودية
    [InlineData("TR")]  // تركيا
    [InlineData("EG")]  // مصر
    [InlineData("JO")]  // الأردن
    [InlineData("LB")]  // لبنان
    public async Task ValidCountryCode_AllSupportedCodes_PassWithNoError(string code)
    {
        var cmd = ValidCommand() with { CountryCode = code };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.CountryCode);
    }

    // ── Currency Codes الصحيحة ────────────────────────────────────────
    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData("EUR")]  // يورو
    [InlineData("USD")]  // دولار
    [InlineData("GBP")]  // جنيه إسترليني
    [InlineData("SYP")]  // ليرة سورية
    [InlineData("TRY")]  // ليرة تركية
    [InlineData("AED")]  // درهم إماراتي
    [InlineData("SAR")]  // ريال سعودي
    [InlineData("EGP")]  // جنيه مصري
    [InlineData("JOD")]  // دينار أردني
    [InlineData("LBP")]  // ليرة لبنانية
    public async Task ValidCurrencyCode_AllSupportedCodes_PassWithNoError(string currency)
    {
        var cmd = ValidCommand() with { CurrencyCode = currency };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.CurrencyCode);
    }

    // ── ForSale: PurchasePrice فقط ────────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForSale_WithPurchasePrice_NoRent_PassesValidation()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 350_000m,
            ColdRent = null,
            WarmRent = null,
            LegalStatus = LegalStatusType.GreenDeed
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveAnyValidationErrors();
    }

    // ── ForRentAndSale: كلا السعرَين موجودان ─────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForRentAndSale_WithBothPrices_PassesValidation()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRentAndSale,
            ColdRent = 900m,
            PurchasePrice = 250_000m,
            LegalStatus = LegalStatusType.CourtJudgment
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveAnyValidationErrors();
    }

    // ── إحداثيات صحيحة: كلاهما موجود ────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task BothCoordinatesProvided_ValidValues_PassWithNoError()
    {
        var cmd = ValidCommand() with
        {
            Latitude = 52.52m,   // برلين
            Longitude = 13.405m
        };

        var result = await _validator.TestValidateAsync(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    // ── إحداثيات: كلاهما null — مقبول ───────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task BothCoordinatesNull_IsOptional_PassWithNoError()
    {
        var cmd = ValidCommand() with { Latitude = null, Longitude = null };

        var result = await _validator.TestValidateAsync(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    // ── حدود الإحداثيات الصحيحة ──────────────────────────────────────
    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(-90, 0)]    // الحد الأدنى للخط العرض
    [InlineData(90, 0)]    // الحد الأقصى للخط العرض
    [InlineData(0, -180)]   // الحد الأدنى للخط الطول
    [InlineData(0, 180)]   // الحد الأقصى للخط الطول
    [InlineData(33.5, 36.3)] // دمشق
    public async Task CoordinatesAtBoundaries_ValidValues_PassWithNoError(
        double lat, double lon)
    {
        var cmd = ValidCommand() with
        {
            Latitude = (decimal)lat,
            Longitude = (decimal)lon
        };

        var result = await _validator.TestValidateAsync(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    // ── Rooms: الحدود المسموحة ────────────────────────────────────────
    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(1)]   // الحد الأدنى
    [InlineData(25)]  // متوسط
    [InlineData(50)]  // الحد الأقصى
    public async Task Rooms_WithinValidRange_PassWithNoError(int rooms)
    {
        var cmd = ValidCommand() with { Rooms = rooms };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.Rooms);
    }

    // ── Area: قيمة صحيحة ──────────────────────────────────────────────
    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(10.1)]  // أدنى قيمة مقبولة (> 10)
    [InlineData(50.0)]  // متوسط
    [InlineData(500.0)] // شقة كبيرة
    public async Task Area_GreaterThan10_PassWithNoError(double area)
    {
        var cmd = ValidCommand() with { Area = (decimal)area };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.Area);
    }

    // ── AmenityIds: null مقبول ────────────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task AmenityIds_Null_PassWithNoError()
    {
        var cmd = ValidCommand() with { AmenityIds = null };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveAnyValidationErrors();
    }

    // ── AmenityIds: قائمة بدون تكرار ─────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task AmenityIds_UniqueIds_PassWithNoError()
    {
        var cmd = ValidCommand() with
        {
            AmenityIds = new List<Guid>
            {
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid()
            }
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.AmenityIds);
    }

    // ── Rooms: null مقبول (اختياري) ──────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rooms_Null_IsOptional_PassWithNoError()
    {
        var cmd = ValidCommand() with { Rooms = null };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.Rooms);
    }

    // ── Area: null مقبول (اختياري) ───────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Area_Null_IsOptional_PassWithNoError()
    {
        var cmd = ValidCommand() with { Area = null };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.Area);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 2: OwnerId — يجب ألا يكون فارغاً
    // ══════════════════════════════════════════════════════════════════
    #region ❌ OwnerId Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task OwnerId_EmptyGuid_FailsWithError()
    {
        var cmd = ValidCommand() with { OwnerId = Guid.Empty };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.OwnerId);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 3: Title — NotEmpty + MaxLength(200)
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Title Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Title_Empty_FailsWithError()
    {
        var cmd = ValidCommand() with { Title = "" };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Title_WhiteSpaceOnly_FailsWithError()
    {
        var cmd = ValidCommand() with { Title = "   " };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Title_Exceeds200Characters_FailsWithError()
    {
        // 201 حرف — يتجاوز الحد
        var cmd = ValidCommand() with { Title = new string('أ', 201) };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Title_Exactly200Characters_PassesValidation()
    {
        // الحد الأقصى بالضبط — مقبول
        var cmd = ValidCommand() with { Title = new string('A', 200) };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.Title);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 4: Description — NotEmpty + MaxLength(5000)
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Description Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Description_Empty_FailsWithError()
    {
        var cmd = ValidCommand() with { Description = "" };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Description_Exceeds5000Characters_FailsWithError()
    {
        var cmd = ValidCommand() with { Description = new string('x', 5001) };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Description);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 5: City — NotEmpty + MaxLength(150)
    // ══════════════════════════════════════════════════════════════════
    #region ❌ City Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task City_Empty_FailsWithError()
    {
        var cmd = ValidCommand() with { City = "" };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.City);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task City_Exceeds150Characters_FailsWithError()
    {
        var cmd = ValidCommand() with { City = new string('B', 151) };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.City);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 6: CountryCode — ISO 3166-1 alpha-2
    // ══════════════════════════════════════════════════════════════════
    #region ❌ CountryCode Failures

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData("XX")]   // رمز غير موجود في القائمة
    [InlineData("ZZ")]   // رمز غير موجود
    [InlineData("DEU")]  // 3 أحرف — يجب أن يكون 2 فقط
    [InlineData("D")]    // حرف واحد فقط
    [InlineData("")]     // فارغ
    [InlineData("12")]   // أرقام بدل حروف
    public async Task CountryCode_InvalidValue_FailsWithError(string code)
    {
        var cmd = ValidCommand() with { CountryCode = code };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.CountryCode);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task CountryCode_LowerCase_PassesValidation()
    {
        // Validator يستخدم OrdinalIgnoreCase → "de" == "DE"
        var cmd = ValidCommand() with { CountryCode = "de" };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.CountryCode);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 7: CurrencyCode — ISO 4217
    // ══════════════════════════════════════════════════════════════════
    #region ❌ CurrencyCode Failures

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData("XYZ")]  // غير موجود في القائمة
    [InlineData("EU")]   // حرفان فقط — يجب 3
    [InlineData("EURO")] // 4 أحرف
    [InlineData("")]     // فارغ
    [InlineData("123")]  // أرقام
    public async Task CurrencyCode_InvalidValue_FailsWithError(string currency)
    {
        var cmd = ValidCommand() with { CurrencyCode = currency };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.CurrencyCode);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task CurrencyCode_LowerCase_PassesValidation()
    {
        // OrdinalIgnoreCase → "eur" == "EUR"
        var cmd = ValidCommand() with { CurrencyCode = "eur" };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.CurrencyCode);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 8: Pricing — قواعد التسعير بحسب ListingType
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Pricing Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForRent_NoColdRentNorWarmRent_FailsOnColdRent()
    {
        // ForRent يتطلب ColdRent > 0
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRent,
            ColdRent = null,
            WarmRent = null
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.ColdRent);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForRent_ColdRentIsZero_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRent,
            ColdRent = 0m,
            WarmRent = 0m
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.ColdRent);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForSale_NoPurchasePrice_FailsWithError()
    {
        // ForSale يتطلب PurchasePrice > 0
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = null,
            ColdRent = null
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.PurchasePrice);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForSale_PurchasePriceIsZero_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 0m,
            ColdRent = null
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.PurchasePrice);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForRentAndSale_OnlyColdRent_NoPurchasePrice_FailsWithError()
    {
        // ForRentAndSale يتطلب كلاً من ColdRent وPurchasePrice
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRentAndSale,
            ColdRent = 900m,
            PurchasePrice = null   // مفقود
        };

        // يجب أن يفشل لأن PurchasePrice مطلوب
        var result = await _validator.TestValidateAsync(cmd);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForRentAndSale_OnlyPurchasePrice_NoColdRent_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRentAndSale,
            ColdRent = null,  // مفقود
            PurchasePrice = 250_000m
        };

        var result = await _validator.TestValidateAsync(cmd);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForRentAndSale_BothPricesZero_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRentAndSale,
            ColdRent = 0m,
            PurchasePrice = 0m
        };

        var result = await _validator.TestValidateAsync(cmd);
        result.ShouldHaveAnyValidationError();
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 9: AmenityIds — لا تكرار
    // ══════════════════════════════════════════════════════════════════
    #region ❌ AmenityIds Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task AmenityIds_WithDuplicateGuids_FailsWithError()
    {
        var duplicateId = Guid.NewGuid();

        var cmd = ValidCommand() with
        {
            AmenityIds = new List<Guid>
            {
                duplicateId,
                Guid.NewGuid(),
                duplicateId   // ← تكرار
            }
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.AmenityIds);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task AmenityIds_AllSameGuid_FailsWithError()
    {
        var sameId = Guid.NewGuid();

        var cmd = ValidCommand() with
        {
            AmenityIds = new List<Guid> { sameId, sameId, sameId }
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.AmenityIds);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 10: Coordinates — كلاهما أو لا شيء + نطاق صحيح
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Coordinate Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task OnlyLatitude_WithoutLongitude_FailsWithError()
    {
        // القاعدة: إما كلاهما أو لا شيء
        var cmd = ValidCommand() with
        {
            Latitude = 52.52m,
            Longitude = null    // مفقود
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveAnyValidationError();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task OnlyLongitude_WithoutLatitude_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            Latitude = null,   // مفقود
            Longitude = 13.405m
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveAnyValidationError();
    }

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(-91.0)]  // أصغر من -90
    [InlineData(91.0)]  // أكبر من 90
    [InlineData(-180.0)] // خارج النطاق الكلي
    [InlineData(200.0)] // خارج النطاق الكلي
    public async Task Latitude_OutOfValidRange_FailsWithError(double lat)
    {
        var cmd = ValidCommand() with
        {
            Latitude = (decimal)lat,
            Longitude = 13.405m   // longitude صحيح — المشكلة في latitude فقط
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Latitude);
    }

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(-181.0)] // أصغر من -180
    [InlineData(181.0)] // أكبر من 180
    public async Task Longitude_OutOfValidRange_FailsWithError(double lon)
    {
        var cmd = ValidCommand() with
        {
            Latitude = 52.52m,
            Longitude = (decimal)lon
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Longitude);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 11: Rooms — نطاق 1-50
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Rooms Failures

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(0)]   // أقل من الحد الأدنى
    [InlineData(-1)]  // قيمة سالبة
    [InlineData(51)]  // أعلى من الحد الأقصى
    [InlineData(100)] // أعلى بكثير
    public async Task Rooms_OutOfValidRange_FailsWithError(int rooms)
    {
        var cmd = ValidCommand() with { Rooms = rooms };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Rooms);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 12: Area — يجب أن يكون أكبر من 10
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Area Failures

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(0)]     // صفر
    [InlineData(-10)]   // سالب
    [InlineData(5)]     // أقل من 10
    [InlineData(10)]    // يساوي 10 (القاعدة: GreaterThan — ليس GreaterThanOrEqual)
    public async Task Area_LessThanOrEqual10_FailsWithError(double area)
    {
        var cmd = ValidCommand() with { Area = (decimal)area };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Area);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔵 المجموعة 13: اختبارات الرسائل — التحقق من نص الخطأ
    // ══════════════════════════════════════════════════════════════════
    #region 🔵 Error Message Verification

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task CountryCode_InvalidCode_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with { CountryCode = "XX" };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.CountryCode)
                  .WithErrorMessage("Invalid ISO 3166-1 country code.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task CurrencyCode_InvalidCode_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with { CurrencyCode = "XYZ" };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.CurrencyCode)
                  .WithErrorMessage("Invalid ISO 4217 currency code.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForRent_NoColdRent_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRent,
            ColdRent = null
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.ColdRent)
                  .WithErrorMessage("Cold rent is required for rental listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task ForSale_NoPurchasePrice_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = null
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.PurchasePrice)
                  .WithErrorMessage("Purchase price is required for sale listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Area_TooSmall_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with { Area = 5m };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Area)
                  .WithErrorMessage("Area must be greater than 10 m².");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task AmenityIds_Duplicates_ContainsExpectedMessage()
    {
        var id = Guid.NewGuid();
        var cmd = ValidCommand() with
        {
            AmenityIds = new List<Guid> { id, id }
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.AmenityIds)
                  .WithErrorMessage("AmenityIds must not contain duplicates.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task OwnerId_Empty_ContainsRequiredMessage()
    {
        var cmd = ValidCommand() with { OwnerId = Guid.Empty };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.OwnerId)
                  .WithErrorMessage("OwnerId is required.");
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🟢🔴 المجموعة 10: مدة الإيجار — RentalStartDate/RentalEndDate/RentalDurationType
    // ══════════════════════════════════════════════════════════════════
    #region Rental Term (ListingType.ForRent / ForRentAndSale)

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_SixMonths_ValidDates_PassesValidation()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var cmd = ValidCommand() with
        {
            RentalStartDate = start,
            RentalEndDate = start.AddMonths(6),
            RentalDurationType = RentalDurationType.SixMonths
        };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_OneYear_ValidDates_PassesValidation()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var cmd = ValidCommand() with
        {
            RentalStartDate = start,
            RentalEndDate = start.AddYears(1),
            RentalDurationType = RentalDurationType.OneYear
        };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_Custom_ArbitraryValidDates_PassesValidation()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var cmd = ValidCommand() with
        {
            RentalStartDate = start,
            RentalEndDate = start.AddDays(45), // مدة عشوائية — Custom لا تفرض طولاً محدداً
            RentalDurationType = RentalDurationType.Custom
        };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_MissingStartDate_FailsWithError()
    {
        var cmd = ValidCommand() with { RentalStartDate = null };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.RentalStartDate)
                  .WithErrorMessage("Rental start date is required for rental listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_MissingEndDate_FailsWithError()
    {
        var cmd = ValidCommand() with { RentalEndDate = null };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.RentalEndDate)
                  .WithErrorMessage("Rental end date is required for rental listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_MissingDurationType_FailsWithError()
    {
        var cmd = ValidCommand() with { RentalDurationType = null };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.RentalDurationType)
                  .WithErrorMessage("Rental duration is required for rental listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_EndDateEqualsStartDate_FailsWithError()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var cmd = ValidCommand() with { RentalStartDate = start, RentalEndDate = start };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveAnyValidationError()
                  .WithErrorMessage("Rental end date must be after the start date.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_EndDateBeforeStartDate_FailsWithError()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var cmd = ValidCommand() with { RentalStartDate = start, RentalEndDate = start.AddDays(-1) };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveAnyValidationError()
                  .WithErrorMessage("Rental end date must be after the start date.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Sale_MustNotRequireRentalDates()
    {
        // بيع بدون أي بيانات إيجار — يجب ألا يفشل بسببها إطلاقاً.
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 300_000m,
            ColdRent = null,
            WarmRent = null,
            LegalStatus = LegalStatusType.GreenDeed,
            RentalStartDate = null,
            RentalEndDate = null,
            RentalDurationType = null
        };

        var result = await _validator.TestValidateAsync(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.RentalStartDate);
        result.ShouldNotHaveValidationErrorFor(x => x.RentalEndDate);
        result.ShouldNotHaveValidationErrorFor(x => x.RentalDurationType);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🟢🔴 المجموعة 11: نوع الملكية (OwnershipType/LegalStatus) — مطلوب للبيع
    // ══════════════════════════════════════════════════════════════════
    #region Ownership Type (LegalStatus)

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(LegalStatusType.GreenDeed)]
    [InlineData(LegalStatusType.CourtJudgment)]
    [InlineData(LegalStatusType.AssociationTransfer)]
    [InlineData(LegalStatusType.Shares)]
    [InlineData(LegalStatusType.Notary)]
    [InlineData(LegalStatusType.InheritanceDocument)]
    [InlineData(LegalStatusType.Pledge)]
    [InlineData(LegalStatusType.YouthHousing)]
    public async Task Sale_ValidOwnershipType_PassesValidation(LegalStatusType ownershipType)
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 300_000m,
            ColdRent = null,
            WarmRent = null,
            LegalStatus = ownershipType
        };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Sale_MissingOwnershipType_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 300_000m,
            ColdRent = null,
            WarmRent = null,
            LegalStatus = null
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.LegalStatus)
                  .WithErrorMessage("Ownership type is required for sale listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Sale_OwnershipTypeUnknown_FailsWithError()
    {
        // Unknown هي القيمة الافتراضية غير المُصرَّح بها — تُعامَل كأنها لم تُحدَّد.
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 300_000m,
            ColdRent = null,
            WarmRent = null,
            LegalStatus = LegalStatusType.Unknown
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.LegalStatus)
                  .WithErrorMessage("Ownership type is required for sale listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Sale_InvalidOwnershipTypeEnumValue_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 300_000m,
            ColdRent = null,
            WarmRent = null,
            LegalStatus = (LegalStatusType)999
        };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.LegalStatus);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Rent_MustNotRequireOwnershipType()
    {
        var cmd = ValidCommand() with { LegalStatus = null };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldNotHaveValidationErrorFor(x => x.LegalStatus);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🟢🔴 المجموعة 12: الأراضي — AreaUnit + Area مطلوبة، Rooms/Bedrooms اختيارية
    // ══════════════════════════════════════════════════════════════════
    #region Land (PropertyType.Category == "Land")

    private static CreatePropertyCommand LandSaleCommand() => ValidCommand() with
    {
        ListingType = ListingType.ForSale,
        PurchasePrice = 80_000m,
        ColdRent = null,
        WarmRent = null,
        LegalStatus = LegalStatusType.GreenDeed,
        PropertyTypeId = 2, // "residential-land" في الـ catalog الوهمي أعلاه — Category = "Land"
        RentalStartDate = null,
        RentalEndDate = null,
        RentalDurationType = null,
        Rooms = null,
        Floor = null
    };

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Land_WithoutRooms_PassesValidation()
    {
        var cmd = LandSaleCommand() with { Area = 1500m, AreaUnit = AreaUnit.SquareMeter };

        var result = await _validator.TestValidateAsync(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Rooms);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(AreaUnit.SquareMeter, 1500)]
    [InlineData(AreaUnit.Qasabah, 100)]
    [InlineData(AreaUnit.Dunum, 2)]
    [InlineData(AreaUnit.Hectare, 1)]
    public async Task Land_AnyAreaUnit_WithPositiveArea_PassesValidation(AreaUnit unit, decimal area)
    {
        var cmd = LandSaleCommand() with { Area = area, AreaUnit = unit };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Land_MissingArea_FailsWithError()
    {
        var cmd = LandSaleCommand() with { Area = null };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Area)
                  .WithErrorMessage("Area is required for land listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Land_ZeroArea_InNonSquareMeterUnit_FailsWithError()
    {
        var cmd = LandSaleCommand() with { Area = 0m, AreaUnit = AreaUnit.Dunum };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.Area)
                  .WithErrorMessage("Area must be greater than zero.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Land_SmallAreaInDunum_BelowTenSquareMeterThreshold_StillPasses()
    {
        // فرق أساسي عن الوحدات المترية: قيمة صغيرة بوحدة غير متر مربع طبيعية تماماً
        // (2 دونم تُعادل تقريباً 2000 م² رغم أن الرقم "2" أصغر من عتبة الـ 10).
        var cmd = LandSaleCommand() with { Area = 2m, AreaUnit = AreaUnit.Dunum };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveValidationErrorFor(x => x.Area);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task AreaUnit_InvalidEnumValue_FailsWithError()
    {
        var cmd = ValidCommand() with { AreaUnit = (AreaUnit)999 };

        (await _validator.TestValidateAsync(cmd))
                  .ShouldHaveValidationErrorFor(x => x.AreaUnit);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task Residential_MissingArea_DoesNotFailTheLandRule()
    {
        // PropertyTypeId=1 في الـ catalog الوهمي = "Residential" — قاعدة الأرض لا تُطبَّق.
        var cmd = ValidCommand() with { Area = null, PropertyTypeId = 1 };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveValidationErrorFor(x => x.Area);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🟢 المجموعة 13: تركيبات ListingType × PropertyType.Category
    // ══════════════════════════════════════════════════════════════════
    #region ListingType × PropertyType combinations

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task RentApartment_PassesValidation()
    {
        var cmd = ValidCommand() with { PropertyTypeId = 1 }; // Residential, ForRent من القاعدة

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task SaleApartment_PassesValidation()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 300_000m,
            ColdRent = null,
            WarmRent = null,
            LegalStatus = LegalStatusType.GreenDeed,
            PropertyTypeId = 1
        };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task SaleLand_PassesValidation()
    {
        var cmd = LandSaleCommand() with { Area = 3m, AreaUnit = AreaUnit.Dunum };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public async Task RentLand_IsASupportedCombination_PassesValidation()
    {
        // Rent+Land مدعومة في هذا التصميم: نفس قواعد الإيجار وقاعدة الأرض تُطبَّقان
        // باستقلالية عن بعضهما، دون حالة خاصة إضافية.
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRent,
            PropertyTypeId = 2,
            Area = 500m,
            AreaUnit = AreaUnit.SquareMeter,
            Rooms = null,
            Floor = null,
            RentalStartDate = start,
            RentalEndDate = start.AddMonths(6),
            RentalDurationType = RentalDurationType.SixMonths
        };

        (await _validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    #endregion
}