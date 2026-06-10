using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Auth.Commands.SendPhoneOtp;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Auth.Tests.Application.Commands;

public sealed class SendPhoneOtpCommandHandlerTests
{
    private static SendPhoneOtpCommandHandler CreateHandler(
        Mock<IOtpCodeRepository> otpRepo,
        Mock<IOtpService> otpService,
        Mock<ISmsService> smsService)
    {
        return new SendPhoneOtpCommandHandler(
            otpRepo.Object,
            otpService.Object,
            smsService.Object,
            NullLogger<SendPhoneOtpCommandHandler>.Instance);
    }

    [Fact(DisplayName = "SMS successful persists OTP exactly once")]
    [Trait("Category", "OtpSmsTransactionalIntegrity")]
    public async Task Handle_SmsSuccessful_PersistsOtpExactlyOnce()
    {
        // Arrange
        var otpRepo = new Mock<IOtpCodeRepository>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var smsService = new Mock<ISmsService>(MockBehavior.Strict);

        otpRepo
            .Setup(x => x.CountRecentAsync("+491701234567", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        otpService
            .Setup(x => x.Generate())
            .Returns(("123456", "hashed-otp"));

        smsService
            .Setup(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        otpRepo
            .Setup(x => x.AddAsync(It.Is<OtpCode>(otp =>
                otp.PhoneNumber == "+491701234567" &&
                otp.CodeHash == "hashed-otp" &&
                otp.Purpose == OtpPurpose.PhoneRegistration),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        otpRepo
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(otpRepo, otpService, smsService);

        // Act
        var result = await handler.Handle(
            new SendPhoneOtpCommand(" +491701234567 ", OtpPurpose.PhoneRegistration),
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);

        smsService.Verify(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()), Times.Once);
        otpRepo.Verify(x => x.AddAsync(It.IsAny<OtpCode>(), It.IsAny<CancellationToken>()), Times.Once);
        otpRepo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "SMS failure does not persist OTP")]
    [Trait("Category", "OtpSmsTransactionalIntegrity")]
    public async Task Handle_SmsFailed_DoesNotPersistOtp()
    {
        // Arrange
        var otpRepo = new Mock<IOtpCodeRepository>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var smsService = new Mock<ISmsService>(MockBehavior.Strict);

        otpRepo
            .Setup(x => x.CountRecentAsync("+491701234567", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        otpService
            .Setup(x => x.Generate())
            .Returns(("123456", "hashed-otp"));

        smsService
            .SetupSequence(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(false)
            .ReturnsAsync(false);

        var handler = CreateHandler(otpRepo, otpService, smsService);

        // Act
        var result = await handler.Handle(
            new SendPhoneOtpCommand("+491701234567", OtpPurpose.PhoneRegistration),
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("SMS_FAILED", result.ErrorCode);

        smsService.Verify(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()), Times.Exactly(3));
        otpRepo.Verify(x => x.AddAsync(It.IsAny<OtpCode>(), It.IsAny<CancellationToken>()), Times.Never);
        otpRepo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "SMS retry success persists OTP")]
    [Trait("Category", "OtpSmsTransactionalIntegrity")]
    public async Task Handle_SmsRetrySuccessful_PersistsOtp()
    {
        // Arrange
        var otpRepo = new Mock<IOtpCodeRepository>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var smsService = new Mock<ISmsService>(MockBehavior.Strict);

        otpRepo
            .Setup(x => x.CountRecentAsync("+491701234567", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        otpService
            .Setup(x => x.Generate())
            .Returns(("123456", "hashed-otp"));

        smsService
            .SetupSequence(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        otpRepo
            .Setup(x => x.AddAsync(It.IsAny<OtpCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        otpRepo
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(otpRepo, otpService, smsService);

        // Act
        var result = await handler.Handle(
            new SendPhoneOtpCommand("+491701234567", OtpPurpose.PhoneRegistration),
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        smsService.Verify(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()), Times.Exactly(2));
        otpRepo.Verify(x => x.AddAsync(It.IsAny<OtpCode>(), It.IsAny<CancellationToken>()), Times.Once);
        otpRepo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "SMS retry exceptions exhausted do not persist OTP")]
    [Trait("Category", "OtpSmsTransactionalIntegrity")]
    public async Task Handle_SmsRetryExhaustedWithExceptions_DoesNotPersistOtp()
    {
        // Arrange
        var otpRepo = new Mock<IOtpCodeRepository>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var smsService = new Mock<ISmsService>(MockBehavior.Strict);

        otpRepo
            .Setup(x => x.CountRecentAsync("+491701234567", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        otpService
            .Setup(x => x.Generate())
            .Returns(("123456", "hashed-otp"));

        smsService
            .SetupSequence(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMS provider unavailable"))
            .ThrowsAsync(new TimeoutException("SMS provider timeout"))
            .ReturnsAsync(false);

        var handler = CreateHandler(otpRepo, otpService, smsService);

        // Act
        var result = await handler.Handle(
            new SendPhoneOtpCommand("+491701234567", OtpPurpose.PhoneRegistration),
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("SMS_FAILED", result.ErrorCode);

        smsService.Verify(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()), Times.Exactly(3));
        otpRepo.Verify(x => x.AddAsync(It.IsAny<OtpCode>(), It.IsAny<CancellationToken>()), Times.Never);
        otpRepo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Rate limited request does not send SMS and does not persist OTP")]
    [Trait("Category", "OtpSmsTransactionalIntegrity")]
    public async Task Handle_RateLimited_DoesNotSendSms_AndDoesNotPersistOtp()
    {
        // Arrange
        var otpRepo = new Mock<IOtpCodeRepository>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var smsService = new Mock<ISmsService>(MockBehavior.Strict);

        otpRepo
            .Setup(x => x.CountRecentAsync("+491701234567", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var handler = CreateHandler(otpRepo, otpService, smsService);

        // Act
        var result = await handler.Handle(
            new SendPhoneOtpCommand("+491701234567", OtpPurpose.PhoneRegistration),
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("RATE_LIMITED", result.ErrorCode);
        Assert.Equal(3600, result.RetryAfterSeconds);

        otpService.Verify(x => x.Generate(), Times.Never);
        smsService.Verify(x => x.SendOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        otpRepo.Verify(x => x.AddAsync(It.IsAny<OtpCode>(), It.IsAny<CancellationToken>()), Times.Never);
        otpRepo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "OTP store failure after SMS success returns failure")]
    [Trait("Category", "OtpSmsTransactionalIntegrity")]
    public async Task Handle_StoreFailureAfterSmsSuccess_ReturnsFailure()
    {
        // Arrange
        var otpRepo = new Mock<IOtpCodeRepository>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var smsService = new Mock<ISmsService>(MockBehavior.Strict);

        otpRepo
            .Setup(x => x.CountRecentAsync("+491701234567", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        otpService
            .Setup(x => x.Generate())
            .Returns(("123456", "hashed-otp"));

        smsService
            .Setup(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        otpRepo
            .Setup(x => x.AddAsync(It.IsAny<OtpCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        otpRepo
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));

        var handler = CreateHandler(otpRepo, otpService, smsService);

        // Act
        var result = await handler.Handle(
            new SendPhoneOtpCommand("+491701234567", OtpPurpose.PhoneRegistration),
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("OTP_STORE_FAILED", result.ErrorCode);

        smsService.Verify(x => x.SendOtpAsync("+491701234567", "123456", It.IsAny<CancellationToken>()), Times.Once);
        otpRepo.Verify(x => x.AddAsync(It.IsAny<OtpCode>(), It.IsAny<CancellationToken>()), Times.Once);
        otpRepo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
