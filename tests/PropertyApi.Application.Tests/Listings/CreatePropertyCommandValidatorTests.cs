using FluentValidation.TestHelper;
using PropertyApi.Application.Listings.Commands.CreateProperty;
using PropertyApi.Application.Listings.Validators;
using PropertyApi.Domain.Enums;
using Xunit;

namespace PropertyApi.Application.Tests.Listings;

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
    // ══════════════════════════════════════════════════════════════════
    private readonly CreatePropertyCommandValidator _validator = new();

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
        AmenityIds: null
    );

    // ══════════════════════════════════════════════════════════════════
    // 🟢 المجموعة 1: الحالات الصحيحة — يجب أن تمر بدون أخطاء
    // ══════════════════════════════════════════════════════════════════
    #region ✅ Valid Cases

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ValidCommand_AllFieldsCorrect_PassesWithNoErrors()
    {
        // Arrange
        var cmd = ValidCommand();

        // Act
        var result = _validator.TestValidate(cmd);

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
    public void ValidCountryCode_AllSupportedCodes_PassWithNoError(string code)
    {
        var cmd = ValidCommand() with { CountryCode = code };

        _validator.TestValidate(cmd)
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
    public void ValidCurrencyCode_AllSupportedCodes_PassWithNoError(string currency)
    {
        var cmd = ValidCommand() with { CurrencyCode = currency };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveValidationErrorFor(x => x.CurrencyCode);
    }

    // ── ForSale: PurchasePrice فقط ────────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForSale_WithPurchasePrice_NoRent_PassesValidation()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 350_000m,
            ColdRent = null,
            WarmRent = null
        };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveAnyValidationErrors();
    }

    // ── ForRentAndSale: كلا السعرَين موجودان ─────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForRentAndSale_WithBothPrices_PassesValidation()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRentAndSale,
            ColdRent = 900m,
            PurchasePrice = 250_000m
        };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveAnyValidationErrors();
    }

    // ── إحداثيات صحيحة: كلاهما موجود ────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void BothCoordinatesProvided_ValidValues_PassWithNoError()
    {
        var cmd = ValidCommand() with
        {
            Latitude = 52.52m,   // برلين
            Longitude = 13.405m
        };

        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    // ── إحداثيات: كلاهما null — مقبول ───────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void BothCoordinatesNull_IsOptional_PassWithNoError()
    {
        var cmd = ValidCommand() with { Latitude = null, Longitude = null };

        var result = _validator.TestValidate(cmd);
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
    public void CoordinatesAtBoundaries_ValidValues_PassWithNoError(
        double lat, double lon)
    {
        var cmd = ValidCommand() with
        {
            Latitude = (decimal)lat,
            Longitude = (decimal)lon
        };

        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    // ── Rooms: الحدود المسموحة ────────────────────────────────────────
    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(1)]   // الحد الأدنى
    [InlineData(25)]  // متوسط
    [InlineData(50)]  // الحد الأقصى
    public void Rooms_WithinValidRange_PassWithNoError(int rooms)
    {
        var cmd = ValidCommand() with { Rooms = rooms };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveValidationErrorFor(x => x.Rooms);
    }

    // ── Area: قيمة صحيحة ──────────────────────────────────────────────
    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(10.1)]  // أدنى قيمة مقبولة (> 10)
    [InlineData(50.0)]  // متوسط
    [InlineData(500.0)] // شقة كبيرة
    public void Area_GreaterThan10_PassWithNoError(double area)
    {
        var cmd = ValidCommand() with { Area = (decimal)area };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveValidationErrorFor(x => x.Area);
    }

    // ── AmenityIds: null مقبول ────────────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void AmenityIds_Null_PassWithNoError()
    {
        var cmd = ValidCommand() with { AmenityIds = null };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveAnyValidationErrors();
    }

    // ── AmenityIds: قائمة بدون تكرار ─────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void AmenityIds_UniqueIds_PassWithNoError()
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

        _validator.TestValidate(cmd)
                  .ShouldNotHaveValidationErrorFor(x => x.AmenityIds);
    }

    // ── Rooms: null مقبول (اختياري) ──────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Rooms_Null_IsOptional_PassWithNoError()
    {
        var cmd = ValidCommand() with { Rooms = null };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveValidationErrorFor(x => x.Rooms);
    }

    // ── Area: null مقبول (اختياري) ───────────────────────────────────
    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Area_Null_IsOptional_PassWithNoError()
    {
        var cmd = ValidCommand() with { Area = null };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveValidationErrorFor(x => x.Area);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 2: OwnerId — يجب ألا يكون فارغاً
    // ══════════════════════════════════════════════════════════════════
    #region ❌ OwnerId Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void OwnerId_EmptyGuid_FailsWithError()
    {
        var cmd = ValidCommand() with { OwnerId = Guid.Empty };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.OwnerId);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 3: Title — NotEmpty + MaxLength(200)
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Title Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Title_Empty_FailsWithError()
    {
        var cmd = ValidCommand() with { Title = "" };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Title_WhiteSpaceOnly_FailsWithError()
    {
        var cmd = ValidCommand() with { Title = "   " };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Title_Exceeds200Characters_FailsWithError()
    {
        // 201 حرف — يتجاوز الحد
        var cmd = ValidCommand() with { Title = new string('أ', 201) };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Title_Exactly200Characters_PassesValidation()
    {
        // الحد الأقصى بالضبط — مقبول
        var cmd = ValidCommand() with { Title = new string('A', 200) };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveValidationErrorFor(x => x.Title);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 4: Description — NotEmpty + MaxLength(5000)
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Description Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Description_Empty_FailsWithError()
    {
        var cmd = ValidCommand() with { Description = "" };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Description_Exceeds5000Characters_FailsWithError()
    {
        var cmd = ValidCommand() with { Description = new string('x', 5001) };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Description);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 5: City — NotEmpty + MaxLength(150)
    // ══════════════════════════════════════════════════════════════════
    #region ❌ City Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void City_Empty_FailsWithError()
    {
        var cmd = ValidCommand() with { City = "" };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.City);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void City_Exceeds150Characters_FailsWithError()
    {
        var cmd = ValidCommand() with { City = new string('B', 151) };

        _validator.TestValidate(cmd)
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
    public void CountryCode_InvalidValue_FailsWithError(string code)
    {
        var cmd = ValidCommand() with { CountryCode = code };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.CountryCode);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void CountryCode_LowerCase_PassesValidation()
    {
        // Validator يستخدم OrdinalIgnoreCase → "de" == "DE"
        var cmd = ValidCommand() with { CountryCode = "de" };

        _validator.TestValidate(cmd)
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
    public void CurrencyCode_InvalidValue_FailsWithError(string currency)
    {
        var cmd = ValidCommand() with { CurrencyCode = currency };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.CurrencyCode);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void CurrencyCode_LowerCase_PassesValidation()
    {
        // OrdinalIgnoreCase → "eur" == "EUR"
        var cmd = ValidCommand() with { CurrencyCode = "eur" };

        _validator.TestValidate(cmd)
                  .ShouldNotHaveValidationErrorFor(x => x.CurrencyCode);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 8: Pricing — قواعد التسعير بحسب ListingType
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Pricing Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForRent_NoColdRentNorWarmRent_FailsOnColdRent()
    {
        // ForRent يتطلب ColdRent > 0
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRent,
            ColdRent = null,
            WarmRent = null
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.ColdRent);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForRent_ColdRentIsZero_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRent,
            ColdRent = 0m,
            WarmRent = 0m
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.ColdRent);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForSale_NoPurchasePrice_FailsWithError()
    {
        // ForSale يتطلب PurchasePrice > 0
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = null,
            ColdRent = null
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.PurchasePrice);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForSale_PurchasePriceIsZero_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 0m,
            ColdRent = null
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.PurchasePrice);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForRentAndSale_OnlyColdRent_NoPurchasePrice_FailsWithError()
    {
        // ForRentAndSale يتطلب كلاً من ColdRent وPurchasePrice
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRentAndSale,
            ColdRent = 900m,
            PurchasePrice = null   // مفقود
        };

        // يجب أن يفشل لأن PurchasePrice مطلوب
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForRentAndSale_OnlyPurchasePrice_NoColdRent_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRentAndSale,
            ColdRent = null,  // مفقود
            PurchasePrice = 250_000m
        };

        var result = _validator.TestValidate(cmd);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForRentAndSale_BothPricesZero_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRentAndSale,
            ColdRent = 0m,
            PurchasePrice = 0m
        };

        var result = _validator.TestValidate(cmd);
        result.ShouldHaveAnyValidationError();
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 9: AmenityIds — لا تكرار
    // ══════════════════════════════════════════════════════════════════
    #region ❌ AmenityIds Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void AmenityIds_WithDuplicateGuids_FailsWithError()
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

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.AmenityIds);
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void AmenityIds_AllSameGuid_FailsWithError()
    {
        var sameId = Guid.NewGuid();

        var cmd = ValidCommand() with
        {
            AmenityIds = new List<Guid> { sameId, sameId, sameId }
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.AmenityIds);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔴 المجموعة 10: Coordinates — كلاهما أو لا شيء + نطاق صحيح
    // ══════════════════════════════════════════════════════════════════
    #region ❌ Coordinate Failures

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void OnlyLatitude_WithoutLongitude_FailsWithError()
    {
        // القاعدة: إما كلاهما أو لا شيء
        var cmd = ValidCommand() with
        {
            Latitude = 52.52m,
            Longitude = null    // مفقود
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveAnyValidationError();
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void OnlyLongitude_WithoutLatitude_FailsWithError()
    {
        var cmd = ValidCommand() with
        {
            Latitude = null,   // مفقود
            Longitude = 13.405m
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveAnyValidationError();
    }

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(-91.0)]  // أصغر من -90
    [InlineData(91.0)]  // أكبر من 90
    [InlineData(-180.0)] // خارج النطاق الكلي
    [InlineData(200.0)] // خارج النطاق الكلي
    public void Latitude_OutOfValidRange_FailsWithError(double lat)
    {
        var cmd = ValidCommand() with
        {
            Latitude = (decimal)lat,
            Longitude = 13.405m   // longitude صحيح — المشكلة في latitude فقط
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Latitude);
    }

    [Theory]
    [Trait("Category", "PropertyValidator")]
    [InlineData(-181.0)] // أصغر من -180
    [InlineData(181.0)] // أكبر من 180
    public void Longitude_OutOfValidRange_FailsWithError(double lon)
    {
        var cmd = ValidCommand() with
        {
            Latitude = 52.52m,
            Longitude = (decimal)lon
        };

        _validator.TestValidate(cmd)
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
    public void Rooms_OutOfValidRange_FailsWithError(int rooms)
    {
        var cmd = ValidCommand() with { Rooms = rooms };

        _validator.TestValidate(cmd)
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
    public void Area_LessThanOrEqual10_FailsWithError(double area)
    {
        var cmd = ValidCommand() with { Area = (decimal)area };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Area);
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════
    // 🔵 المجموعة 13: اختبارات الرسائل — التحقق من نص الخطأ
    // ══════════════════════════════════════════════════════════════════
    #region 🔵 Error Message Verification

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void CountryCode_InvalidCode_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with { CountryCode = "XX" };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.CountryCode)
                  .WithErrorMessage("Invalid ISO 3166-1 country code.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void CurrencyCode_InvalidCode_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with { CurrencyCode = "XYZ" };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.CurrencyCode)
                  .WithErrorMessage("Invalid ISO 4217 currency code.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForRent_NoColdRent_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRent,
            ColdRent = null
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.ColdRent)
                  .WithErrorMessage("Cold rent is required for rental listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void ForSale_NoPurchasePrice_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = null
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.PurchasePrice)
                  .WithErrorMessage("Purchase price is required for sale listings.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void Area_TooSmall_ContainsExpectedMessage()
    {
        var cmd = ValidCommand() with { Area = 5m };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Area)
                  .WithErrorMessage("Area must be greater than 10 m².");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void AmenityIds_Duplicates_ContainsExpectedMessage()
    {
        var id = Guid.NewGuid();
        var cmd = ValidCommand() with
        {
            AmenityIds = new List<Guid> { id, id }
        };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.AmenityIds)
                  .WithErrorMessage("AmenityIds must not contain duplicates.");
    }

    [Fact]
    [Trait("Category", "PropertyValidator")]
    public void OwnerId_Empty_ContainsRequiredMessage()
    {
        var cmd = ValidCommand() with { OwnerId = Guid.Empty };

        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.OwnerId)
                  .WithErrorMessage("OwnerId is required.");
    }

    #endregion
}