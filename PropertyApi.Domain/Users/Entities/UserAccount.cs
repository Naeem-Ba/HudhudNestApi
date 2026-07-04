
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Domain.Users.Entities;

/// <summary>Business profile of a platform user. Contains no authentication state.</summary>
public sealed class UserAccount
{
    private UserAccount() { }

    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string? TaxNumber { get; private set; }
    public string? ProfileImageUrl { get; private set; }
    public string? WhatsAppNumber { get; private set; }
    public string PreferredLanguage { get; private set; } = "en";
    public string PreferredCurrency { get; private set; } = "EUR";
    public string? CountryCode { get; private set; }
    public bool IsBanned { get; private set; }
    public string? BanReason { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static UserAccount Create(Guid id, string firstName, string lastName, DateTime utcNow)
    {
        if (id == Guid.Empty) throw new ArgumentException("User id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("First name is required.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Last name is required.", nameof(lastName));

        return new UserAccount
        {
            Id = id,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void UpdateProfile(string firstName, string lastName, string? displayName, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("First name is required.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Last name is required.", nameof(lastName));
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        UpdatedAt = utcNow;
    }

    public void Ban(string reason, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Ban reason is required.", nameof(reason));
        IsBanned = true;
        BanReason = reason.Trim();
        UpdatedAt = utcNow;
    }

    public void Unban(DateTime utcNow)
    {
        IsBanned = false;
        BanReason = null;
        UpdatedAt = utcNow;
    }

    public void RecordSuccessfulLogin(DateTime utcNow)
    {
        LastLoginAt = utcNow;
        UpdatedAt = utcNow;
    }
}
