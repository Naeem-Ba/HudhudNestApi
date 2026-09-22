using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using HudhudNestApi.Application.Listings.Commands.DeletePropertyImage;
using HudhudNestApi.Application.Listings.Commands.UploadPropertyImages;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Queries.GetPropertyImages;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Integration.Tests.Controllers;

[Trait("Feature", "PropertyImages")]
public sealed class PropertyImagesControllerTests
{
    [Fact(DisplayName = "Upload delegates to ISender and returns uploaded images")]
    public async Task Upload_ValidRequest_Delegates_To_ISender()
    {
        var propertyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var image = new PropertyImageDto(Guid.NewGuid(), "https://cdn.example.com/property.jpg", true, 0);

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<UploadPropertyImagesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadPropertyImagesResult.Success([image]));

        var controller = CreateController(sender.Object, userId);
        var files = CreateImageFiles("property.jpg", "image/jpeg", 128);

        var response = await controller.Upload(propertyId, files, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var images = Assert.IsAssignableFrom<IReadOnlyList<PropertyImageDto>>(ok.Value);
        Assert.Single(images);

        sender.Verify(x => x.Send(
            It.Is<UploadPropertyImagesCommand>(command =>
                command.PropertyId == propertyId &&
                command.UserId == userId &&
                command.Files.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Delete maps Forbidden handler result to ForbidResult")]
    public async Task Delete_ForbiddenResult_Returns_Forbid()
    {
        var propertyId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<DeletePropertyImageCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PropertyImageMutationResult.Forbidden());

        var controller = CreateController(sender.Object, userId);

        var response = await controller.Delete(propertyId, imageId, CancellationToken.None);

        Assert.IsType<ForbidResult>(response);
    }

    [Fact(DisplayName = "GetAll returns public property images ordered by handler")]
    public async Task GetAll_PublicProperty_Returns_Images()
    {
        var propertyId = Guid.NewGuid();
        var images = new List<PropertyImageDto>
        {
            new(Guid.NewGuid(), "https://cdn.example.com/1.jpg", true, 1),
            new(Guid.NewGuid(), "https://cdn.example.com/2.jpg", false, 2)
        };

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<GetPropertyImagesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PropertyImagesQueryResult.Success(images));

        var controller = CreateController(sender.Object, userId: null);

        var response = await controller.GetAll(propertyId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var result = Assert.IsAssignableFrom<IReadOnlyList<PropertyImageDto>>(ok.Value);
        Assert.Equal(2, result.Count);
    }

    private static PropertyImagesController CreateController(ISender sender, Guid? userId)
    {
        var controller = new PropertyImagesController(sender);
        var httpContext = new DefaultHttpContext();

        if (userId.HasValue)
        {
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())],
                    authenticationType: "TestAuth"));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    private static IFormFileCollection CreateImageFiles(
        string fileName,
        string contentType,
        int sizeInBytes)
    {
        var content = Enumerable.Repeat((byte)1, sizeInBytes).ToArray();
        var stream = new MemoryStream(content);

        var file = new FormFile(stream, 0, stream.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        return new FormFileCollection { file };
    }
}
