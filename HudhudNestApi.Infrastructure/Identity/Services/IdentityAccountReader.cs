using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Infrastructure.Identity.Entities;

namespace HudhudNestApi.Infrastructure.Identity.Services;

public sealed class IdentityAccountReader
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IPhoneNumberLookupHasher _phoneLookupHasher;

    public IdentityAccountReader(
        UserManager<ApplicationUser> users,
        IPhoneNumberLookupHasher phoneLookupHasher)
    {
        _users = users;
        _phoneLookupHasher = phoneLookupHasher;
    }

    public async Task<IdentityAccountSnapshot?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await _users.FindByIdAsync(id.ToString());
        return user is null ? null : IdentityAdapterMapping.Snapshot(user);
    }

    public async Task<IdentityAccountSnapshot?> FindByEmailAsync(string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ct.ThrowIfCancellationRequested();
        var user = await _users.FindByEmailAsync(email.Trim());
        return user is null ? null : IdentityAdapterMapping.Snapshot(user);
    }

    public async Task<IdentityAccountSnapshot?> FindByLoginAsync(
        string provider,
        string providerKey,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ct.ThrowIfCancellationRequested();
        var user = await _users.FindByLoginAsync(provider, providerKey);
        return user is null ? null : IdentityAdapterMapping.Snapshot(user);
    }

    public async Task<IdentityAccountSnapshot?> FindByPhoneNumberAsync(string phone, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);
        var hash = _phoneLookupHasher.Compute(phone);
        var user = await _users.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedPhoneNumber == phone ||
                         candidate.PhoneNumberLookupHash == hash,
            ct);
        return user is null ? null : IdentityAdapterMapping.Snapshot(user);
    }
}
