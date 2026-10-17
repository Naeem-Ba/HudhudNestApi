using HudhudNestApi.Application.Bookings.Commands.RequestVisit;
using HudhudNestApi.Application.Users.Messaging.Commands.SendMessage;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>QA Phase 8, P8-D01: a NUL character in a message or a visit request used to end as a 500.</summary>
public sealed class NulCharacterInVisitAndMessageTests
{
    [Fact]
    public void MessageContentWithNul_IsRefused()
    {
        var result = new SendMessageCommandValidator().Validate(new SendMessageCommand(Guid.NewGuid(), null, "hello\0world"));

        Assert.Contains(result.Errors, e => e.PropertyName == "Content" && e.ErrorCode == "INVALID_CHARACTER");
    }

    [Fact]
    public void OrdinaryMessage_Passes()
        => Assert.Empty(new SendMessageCommandValidator().Validate(new SendMessageCommand(Guid.NewGuid(), null, "مرحبا")).Errors);

    [Fact]
    public void VisitNameAndNoteWithNul_AreRefused()
    {
        var command = new RequestVisitCommand(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(3), "Sam\0", "+49 170 1234567", "note\0");

        var result = new RequestVisitCommandValidator().Validate(command);

        Assert.Contains(result.Errors, e => e.PropertyName == "VisitorName" && e.ErrorCode == "INVALID_CHARACTER");
        Assert.Contains(result.Errors, e => e.PropertyName == "VisitorNote" && e.ErrorCode == "INVALID_CHARACTER");
    }
}
