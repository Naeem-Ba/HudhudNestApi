using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudhudNestApi.Application.Auth.Interfaces;


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
    /// <summary>
    /// <paramref name="language"/> is the site language the client was showing ("ar", "en"
    /// or "de", regional forms accepted). Null or unsupported keeps the bilingual message.
    /// </summary>
    Task SendVerificationLinkAsync(
        string email,
        string verificationToken,
        string? language,
        CancellationToken ct = default);
}