using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Auth.Interfaces;


public interface IOtpService
{

    (string otp, string hash) Generate();

    bool Verify(string otp, string storedHash);
}

public interface ISmsService
{

    Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default);
}

public interface IEmailVerificationService
{
    Task SendVerificationLinkAsync(
        string email,
        string verificationToken,
        CancellationToken ct = default);
}