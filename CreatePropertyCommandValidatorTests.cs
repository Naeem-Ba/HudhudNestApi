// ============================================================
// PropertyApi.Tests/Validators/CreatePropertyCommandValidatorTests.cs
//
// اختبارات وحدة لـ CreatePropertyCommandValidator
// تستخدم xUnit + FluentValidation.TestHelper
//
// تشغيل: dotnet test --filter "Category=Validators"
// ============================================================

using FluentValidation.TestHelper;
using PropertyApi.Application.Listings.Commands.CreateProperty;
using PropertyApi.Application.Listings.Validators;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Tests.Validators;

/// <summary>
/// اختبارات التحقق من صحة أمر إنشاء عقار جديد.
/// كل اختبار يتحقق من حالة واحدة فقط (مبدأ Single Responsibility في الاختبارات).
/// </summary>
public sealed class CreatePropertyCommandValidatorTests
{
    // ── أداة الاختبار (Validator) ────────────────────────────
    private readonly CreatePropertyCommandValidator _validator = new();

    // ── بيانات أساسية صحيحة تُستخدم كنقطة انطلاق ──────────
    private static CreatePropertyCommand ValidCommand() => new()
    {
        OwnerId = Guid.NewGuid(),
        Title = "شقة فاخرة في برلين",
        Description = "شقة واسعة مطلة على الحديقة، 3 غرف نوم",
        City = "Berlin",
        CountryCode = "DE",
        CurrencyCode = "EUR",
        ListingType = ListingType.ForRent,
        ColdRent = 1200,
        Rooms = 3,
        Area = 85
    };

    // ============================================================
    // 🟢 اختبارات النجاح — بيانات صحيحة يجب أن تمر
    // ============================================================

    [Fact]
    [Trait("Category", "Validators")]
    public void ValidCommand_ShouldPass_WithNoErrors()
    {
        // Arrange
        var cmd = ValidCommand();

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [Trait("Category", "Validators")]
    [InlineData("DE")]
    [InlineData("US")]
    [InlineData("SY")]
    [InlineData("AE")]
    public void ValidCountryCode_ShouldPass(string code)
    {
        var cmd = ValidCommand() with { CountryCode = code };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.CountryCode);
    }

    [Theory]
    [Trait("Category", "Validators")]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("GBP")]
    public void ValidCurrencyCode_ShouldPass(string currency)
    {
        var cmd = ValidCommand() with { CurrencyCode = currency };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.CurrencyCode);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void ForSale_WithPurchasePrice_ShouldPass()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 350_000,
            ColdRent = 0
        };
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void BothCoordinates_ShouldPass()
    {
        var cmd = ValidCommand() with { Latitude = 52.52, Longitude = 13.405 };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.Latitude);
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    // ============================================================
    // 🔴 اختبارات الفشل — بيانات خاطئة يجب أن ترفضها
    // ============================================================

    [Fact]
    [Trait("Category", "Validators")]
    public void EmptyOwnerId_ShouldFail()
    {
        var cmd = ValidCommand() with { OwnerId = Guid.Empty };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.OwnerId);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void EmptyTitle_ShouldFail()
    {
        var cmd = ValidCommand() with { Title = "" };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void TitleTooLong_ShouldFail()
    {
        var cmd = ValidCommand() with { Title = new string('أ', 201) };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Theory]
    [Trait("Category", "Validators")]
    [InlineData("XX")]   // غير موجود
    [InlineData("ZZ")]   // غير موجود
    [InlineData("DEU")]  // ثلاثة حروف — خطأ
    [InlineData("")]     // فارغ
    public void InvalidCountryCode_ShouldFail(string code)
    {
        var cmd = ValidCommand() with { CountryCode = code };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.CountryCode);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void ForRent_WithoutColdRent_ShouldFail()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForRent,
            ColdRent = 0,
            WarmRent = 0
        };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.ColdRent);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void ForSale_WithoutPurchasePrice_ShouldFail()
    {
        var cmd = ValidCommand() with
        {
            ListingType = ListingType.ForSale,
            PurchasePrice = 0,
            ColdRent = 0
        };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.PurchasePrice);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void OnlyLatitude_WithoutLongitude_ShouldFail()
    {
        // القاعدة: كلاهما أو لا شيء
        var cmd = ValidCommand() with { Latitude = 52.52, Longitude = null };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveAnyValidationError(); // يجب أن يفشل بسبب الكوورديناتس الناقصة
    }

    [Theory]
    [Trait("Category", "Validators")]
    [InlineData(-91)]  // أصغر من -90
    [InlineData(91)]   // أكبر من 90
    public void InvalidLatitude_ShouldFail(double lat)
    {
        var cmd = ValidCommand() with { Latitude = lat, Longitude = 13.0 };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void RoomsOutOfRange_ShouldFail()
    {
        var cmd = ValidCommand() with { Rooms = 51 }; // Max = 50
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Rooms);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void AreaTooSmall_ShouldFail()
    {
        var cmd = ValidCommand() with { Area = 5 }; // Min = 10
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Area);
    }
}