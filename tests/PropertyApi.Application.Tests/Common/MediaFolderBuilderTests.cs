using PropertyApi.Application.Common.Enums;
using PropertyApi.Application.Common.Models;
using PropertyApi.Application.Common.Services;
using Xunit;

namespace PropertyApi.Application.Tests.Common;

/// <summary>
/// Covers the deterministic folder layout every upload handler now shares — the storage-path
/// contract other tests (UploadUserAvatarCommandHandlerTests, AgencyTests, etc.) build their own
/// expected-folder assertions from.
/// </summary>
public sealed class MediaFolderBuilderTests
{
    private readonly MediaFolderBuilder _sut = new();

    [Theory]
    [InlineData(MediaEntityType.Agency, "agencies")]
    [InlineData(MediaEntityType.User, "users")]
    [InlineData(MediaEntityType.Property, "properties")]
    [InlineData(MediaEntityType.ShortStayListing, "short-stay")]
    [InlineData(MediaEntityType.Investment, "investments")]
    [InlineData(MediaEntityType.ServiceRequest, "services")]
    public void BuildFolder_UsesTheExpectedEntitySegment(MediaEntityType entityType, string expectedSegment)
    {
        var entityId = Guid.NewGuid();

        var folder = _sut.BuildFolder(entityType, entityId, MediaCategories.Images);

        Assert.Equal($"realestateworld/{expectedSegment}/{entityId:D}/{MediaCategories.Images}", folder);
    }

    [Fact]
    public void BuildFolder_ForSocialAssets_NestsUnderSystemSocial_KeptOutOfThePropertysOwnFolder()
    {
        var propertyId = Guid.NewGuid();

        var folder = _sut.BuildFolder(MediaEntityType.Social, propertyId, MediaCategories.Share);

        Assert.Equal($"realestateworld/system/social/{propertyId:D}/{MediaCategories.Share}", folder);
        Assert.DoesNotContain("realestateworld/properties/", folder);
    }

    [Fact]
    public void BuildFolder_TwoDifferentEntitiesOfTheSameType_NeverProduceTheSameFolder()
    {
        var folderA = _sut.BuildFolder(MediaEntityType.Agency, Guid.NewGuid(), MediaCategories.Logo);
        var folderB = _sut.BuildFolder(MediaEntityType.Agency, Guid.NewGuid(), MediaCategories.Logo);

        Assert.NotEqual(folderA, folderB);
    }

    [Fact]
    public void BuildFolder_DifferentCategoriesForTheSameEntity_ProduceDifferentFolders()
    {
        var agencyId = Guid.NewGuid();

        var logoFolder = _sut.BuildFolder(MediaEntityType.Agency, agencyId, MediaCategories.Logo);
        var documentsFolder = _sut.BuildFolder(MediaEntityType.Agency, agencyId, MediaCategories.Documents);

        Assert.NotEqual(logoFolder, documentsFolder);
    }

    [Fact]
    public void BuildFolder_WithEmptyEntityId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => _sut.BuildFolder(MediaEntityType.Property, Guid.Empty, MediaCategories.Images));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Images")] // uppercase not allowed — keeps Cloudinary paths predictable
    [InlineData("../etc")] // path traversal attempt
    [InlineData("images/nested")] // slashes not allowed — category is a single segment
    [InlineData("images with spaces")]
    public void BuildFolder_WithAnInvalidCategory_Throws(string invalidCategory)
    {
        Assert.Throws<ArgumentException>(
            () => _sut.BuildFolder(MediaEntityType.Property, Guid.NewGuid(), invalidCategory));
    }

    [Fact]
    public void BuildFolder_NeverAcceptsAnyClientSuppliedFolderOverride()
    {
        // Structural guard: IMediaFolderBuilder.BuildFolder's only inputs are the (already
        // authorized) entity type/id and a fixed category constant — there is no overload or
        // parameter that could take a raw folder string from a request. This test exists so a
        // future change adding such a parameter fails a code review conversation, not just an
        // eyeball check.
        var method = typeof(PropertyApi.Application.Common.Interfaces.IMediaFolderBuilder)
            .GetMethod(nameof(PropertyApi.Application.Common.Interfaces.IMediaFolderBuilder.BuildFolder));

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(MediaEntityType), parameters[0].ParameterType);
        Assert.Equal(typeof(Guid), parameters[1].ParameterType);
        Assert.Equal(typeof(string), parameters[2].ParameterType);
    }
}
