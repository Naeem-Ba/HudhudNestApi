using FluentValidation.TestHelper;
using Moq;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Valuation.Commands.CreateValuationInquiry;
using HudhudNestApi.Application.Valuation.Commands.SubmitValuationContactConsent;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Tests.Valuation;

/// <summary>
/// QA Phase 11 (valuation): the anonymous create endpoint answered 500 for ids that do not exist and for
/// absurd sizes, and the contact-consent endpoint stored whatever text it was given and sent it to the office.
/// </summary>
public sealed class ValuationPhase11ValidatorTests
{
    private static CreateValuationInquiryCommandValidator CreateValidator(bool locationsConsistent = true, params int[] propertyTypeIds)
    {
        var locations = new Mock<ILocationHierarchyChecker>();
        locations
            .Setup(x => x.IsConsistentAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationsConsistent);

        var lookups = new Mock<ICommonLookupService>();
        lookups
            .Setup(x => x.GetPropertyTypeCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(propertyTypeIds.Select(id => new PropertyTypeLookupDto(id, "t", "ن", "t", "Residential", null)).ToList());

        return new CreateValuationInquiryCommandValidator(locations.Object, lookups.Object);
    }

    private static CreateValuationInquiryCommand ValidCreate() =>
        new(null, 1, 1, null, 1, 90m, 3, ListingType.ForSale);

    [Fact]
    public async Task Create_ValidRequest_Passes()
        => (await CreateValidator(true, 1).TestValidateAsync(ValidCreate())).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public async Task Create_UnknownOrMismatchedLocation_Fails()
        => (await CreateValidator(false, 1).TestValidateAsync(ValidCreate()))
            .ShouldHaveValidationErrorFor("Location");

    [Fact]
    public async Task Create_UnknownPropertyType_Fails()
        => (await CreateValidator(true, 1).TestValidateAsync(ValidCreate() with { PropertyTypeId = 9999 }))
            .ShouldHaveValidationErrorFor(x => x.PropertyTypeId);

    [Fact]
    public async Task Create_NoPropertyType_IsAllowed()
        => (await CreateValidator(true, 1).TestValidateAsync(ValidCreate() with { PropertyTypeId = null }))
            .ShouldNotHaveValidationErrorFor(x => x.PropertyTypeId);

    [Theory]
    [InlineData(100_000_000)]
    [InlineData(1_000_000_000_000)]
    public async Task Create_AreaAboveTheColumn_Fails(long area)
        => (await CreateValidator(true, 1).TestValidateAsync(ValidCreate() with { Area = area }))
            .ShouldHaveValidationErrorFor(x => x.Area);

    [Fact]
    public async Task Create_AreaAtDecimalMaximum_Fails()
        => (await CreateValidator(true, 1).TestValidateAsync(ValidCreate() with { Area = decimal.MaxValue }))
            .ShouldHaveValidationErrorFor(x => x.Area);

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(int.MaxValue)]
    public async Task Create_RoomsOutsideOneToFifty_Fails(int rooms)
        => (await CreateValidator(true, 1).TestValidateAsync(ValidCreate() with { Rooms = rooms }))
            .ShouldHaveValidationErrorFor(x => x.Rooms);

    private static SubmitValuationContactConsentCommand Consent(string? phone, string? email)
        => new(Guid.NewGuid(), Guid.NewGuid(), null, phone, email);

    private readonly SubmitValuationContactConsentCommandValidator _consent = new();

    [Theory]
    [InlineData("+963 900 000 009")]
    [InlineData("0900000009")]
    public async Task Consent_WellFormedPhone_Passes(string phone)
        => (await _consent.TestValidateAsync(Consent(phone, null))).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("abc")]
    [InlineData("<script>")]
    [InlineData("12")]
    public async Task Consent_MalformedPhone_Fails(string phone)
        => (await _consent.TestValidateAsync(Consent(phone, null))).ShouldHaveValidationErrorFor(x => x.ContactPhone);

    [Fact]
    public async Task Consent_MalformedEmail_Fails()
        => (await _consent.TestValidateAsync(Consent(null, "nope"))).ShouldHaveValidationErrorFor(x => x.ContactEmail);

    [Fact]
    public async Task Consent_EmailLongerThanTheColumn_Fails()
        => (await _consent.TestValidateAsync(Consent(null, new string('a', 320) + "@x.test")))
            .ShouldHaveValidationErrorFor(x => x.ContactEmail);

    [Fact]
    public async Task Consent_OneWellFormedChannel_Passes()
        => (await _consent.TestValidateAsync(Consent(null, "client@example.test"))).ShouldNotHaveAnyValidationErrors();
}
