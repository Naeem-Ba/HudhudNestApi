using FluentValidation.TestHelper;
using Moq;
using PropertyApi.Application.Agencies.Commands.CreateAgency;
using PropertyApi.Application.Agencies.Commands.UpdateAgency;
using PropertyApi.Application.Agencies.Validators;
using PropertyApi.Application.Common.DTOs;
using PropertyApi.Application.Common.Interfaces;
using Xunit;

namespace PropertyApi.Application.Tests.Agencies;

/// <summary>
/// المرحلة 1 (إغلاق الفجوة الجغرافية على Agency) — يغطي قواعد الاتساق الهرمي
/// (District ينتمي إلى Governorate، Neighborhood ينتمي إلى District) على كلا
/// المُدقِّقين. لا يعيد اختبار Agency.Create/UpdateProfile نفسيهما (مغطّاة في
/// AgencyTests.cs) — هذا الملف مخصَّص للحقول الجغرافية الجديدة فقط.
///
/// Governorate 1 له District 10 (وهمي)، District 10 له Neighborhood 100 — يحاكي
/// GetDistrictsAsync/GetNeighborhoodsAsync الحقيقيين في ICommonLookupService دون
/// الحاجة لقاعدة بيانات.
/// </summary>
public sealed class AgencyLocationValidatorTests
{
    private const int GovernorateId = 1;
    private const int OtherGovernorateId = 2;
    private const int DistrictId = 10;
    private const int OtherDistrictId = 20;
    private const int NeighborhoodId = 100;

    private static Mock<ICommonLookupService> BuildLookupsMock()
    {
        var mock = new Mock<ICommonLookupService>();

        mock.Setup(x => x.GetDistrictsAsync(GovernorateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StructuredLocationLookupDto>
            {
                new(DistrictId, "منطقة أ", "District A", GovernorateId)
            });

        mock.Setup(x => x.GetDistrictsAsync(OtherGovernorateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StructuredLocationLookupDto>
            {
                new(OtherDistrictId, "منطقة ب", "District B", OtherGovernorateId)
            });

        mock.Setup(x => x.GetNeighborhoodsAsync(DistrictId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StructuredLocationLookupDto>
            {
                new(NeighborhoodId, "حي س", "Neighborhood S", DistrictId)
            });

        mock.Setup(x => x.GetNeighborhoodsAsync(OtherDistrictId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StructuredLocationLookupDto>());

        return mock;
    }

    // ══════════════════════════════════════════════════════════════
    // CreateAgencyCommandValidator
    // ══════════════════════════════════════════════════════════════

    private static CreateAgencyCommand ValidCreateCommand() => new(
        Name: "مكتب الشام العقاري",
        Slug: "sham-realty",
        CountryCode: "SY",
        Description: null,
        ContactEmail: null,
        ContactPhone: null,
        City: null,
        LicenseNumber: null,
        RequestingUserId: Guid.NewGuid(),
        IpAddress: null);

    [Fact]
    public async Task Create_NoLocationAtAll_PassesValidation()
    {
        // مكتب "غير مصنَّف جغرافيًا" — الحالة القديمة، يجب أن تبقى صحيحة (البند 13/14).
        var validator = new CreateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidCreateCommand();

        (await validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Create_GovernorateOnly_PassesValidation()
    {
        // Test 1 — Governorate only.
        var validator = new CreateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidCreateCommand() with { GovernorateId = GovernorateId };

        (await validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Create_GovernorateAndDistrict_PassesValidation()
    {
        var validator = new CreateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidCreateCommand() with { GovernorateId = GovernorateId, DistrictId = DistrictId };

        (await validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Create_FullLocation_PassesValidation()
    {
        // Test 2 — Full location.
        var validator = new CreateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidCreateCommand() with
        {
            GovernorateId = GovernorateId,
            DistrictId = DistrictId,
            NeighborhoodId = NeighborhoodId
        };

        (await validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Create_DistrictWithoutGovernorate_Fails()
    {
        var validator = new CreateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidCreateCommand() with { GovernorateId = null, DistrictId = DistrictId };

        (await validator.TestValidateAsync(cmd)).ShouldHaveValidationErrorFor("DistrictId");
    }

    [Fact]
    public async Task Create_DistrictNotBelongingToGovernorate_Fails()
    {
        // Test 4 — Invalid hierarchy: District B لا ينتمي إلى Governorate A.
        var validator = new CreateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidCreateCommand() with { GovernorateId = GovernorateId, DistrictId = OtherDistrictId };

        (await validator.TestValidateAsync(cmd)).ShouldHaveValidationErrorFor("DistrictId");
    }

    [Fact]
    public async Task Create_NeighborhoodWithoutDistrict_Fails()
    {
        var validator = new CreateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidCreateCommand() with { GovernorateId = GovernorateId, NeighborhoodId = NeighborhoodId };

        (await validator.TestValidateAsync(cmd)).ShouldHaveValidationErrorFor("NeighborhoodId");
    }

    [Fact]
    public async Task Create_NeighborhoodNotBelongingToDistrict_Fails()
    {
        // Test 5 — Invalid Neighborhood.
        var validator = new CreateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidCreateCommand() with
        {
            GovernorateId = OtherGovernorateId,
            DistrictId = OtherDistrictId,
            NeighborhoodId = NeighborhoodId // ينتمي إلى DistrictId=10 لا 20
        };

        (await validator.TestValidateAsync(cmd)).ShouldHaveValidationErrorFor("NeighborhoodId");
    }

    // ══════════════════════════════════════════════════════════════
    // UpdateAgencyCommandValidator — نفس القواعد، أمر مختلف
    // ══════════════════════════════════════════════════════════════

    private static UpdateAgencyCommand ValidUpdateCommand() => new(
        AgencyId: Guid.NewGuid(),
        Name: "مكتب الشام العقاري",
        Description: null,
        ContactEmail: null,
        ContactPhone: null,
        City: null,
        RequestingUserId: Guid.NewGuid());

    [Fact]
    public async Task Update_ClearingLocationEntirely_PassesValidation()
    {
        // إزالة كاملة (البند 7) — null/null/null يجب أن تمر دومًا.
        var validator = new UpdateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidUpdateCommand();

        (await validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Update_FullValidLocation_PassesValidation()
    {
        // Test 6 — Update من null/null/null إلى موقع كامل.
        var validator = new UpdateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidUpdateCommand() with
        {
            GovernorateId = GovernorateId,
            DistrictId = DistrictId,
            NeighborhoodId = NeighborhoodId
        };

        (await validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Update_DistrictNotBelongingToGovernorate_Fails()
    {
        var validator = new UpdateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidUpdateCommand() with { GovernorateId = GovernorateId, DistrictId = OtherDistrictId };

        (await validator.TestValidateAsync(cmd)).ShouldHaveValidationErrorFor("DistrictId");
    }

    [Fact]
    public async Task Update_NeighborhoodNotBelongingToDistrict_Fails()
    {
        var validator = new UpdateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidUpdateCommand() with
        {
            GovernorateId = GovernorateId,
            DistrictId = DistrictId,
            NeighborhoodId = 999 // غير موجود ضمن District=10 أصلاً
        };

        (await validator.TestValidateAsync(cmd)).ShouldHaveValidationErrorFor("NeighborhoodId");
    }

    [Fact]
    public async Task Update_RemovingOnlyNeighborhood_KeepingGovernorateAndDistrict_PassesValidation()
    {
        // إزالة Neighborhood فقط مع الإبقاء على Governorate+District — حالة صحيحة.
        var validator = new UpdateAgencyCommandValidator(BuildLookupsMock().Object);
        var cmd = ValidUpdateCommand() with
        {
            GovernorateId = GovernorateId,
            DistrictId = DistrictId,
            NeighborhoodId = null
        };

        (await validator.TestValidateAsync(cmd)).ShouldNotHaveAnyValidationErrors();
    }
}
