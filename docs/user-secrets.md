# User Secrets Setup

This document explains how to configure local development secrets for the PropertyApi backend.

## Purpose

Sensitive values must not be committed to Git.

Examples of sensitive values:

- Database passwords
- JWT signing keys
- Cloudinary credentials
- Admin seed passwords
- API secrets

For local development, use the .NET User Secrets store.

## Initialize User Secrets

Run this command from the repository root:

```powershell
dotnet user-secrets init --project .\PropertyApi\PropertyApi.csproj
```

## Email delivery (Resend)

`Email:Provider` selects the sender: `Console`, `Smtp` or `Resend`. `Console` only writes
messages to the log and the application refuses to start with it in Production.

The Resend API key is a secret and must never be committed. Set it locally with:

```powershell
dotnet user-secrets set "Email:Provider" "Resend" --project .\PropertyApi\PropertyApi.csproj
dotnet user-secrets set "Email:Resend:ApiKey" "re_your_key_here" --project .\PropertyApi\PropertyApi.csproj
```

When deployed, supply the same values as environment variables instead:
`Email__Provider`, `Email__Resend__ApiKey`.

Two settings decide whether a confirmation email is usable, and both fail loudly rather
than silently:

- `Email:From` must be an address on a domain verified in Resend. Sending from
  `onboarding@resend.dev` works, but Resend only delivers those messages to the account
  owner's own address, so real users receive nothing.
- `Frontend:BaseUrl` is the origin the confirmation link points at. It is required in
  Production and must use HTTPS. It is deliberately never derived from the request's
  `Host` header, which a proxy could let an attacker set.

Add to the list of values that must stay out of Git:

- `Email:Resend:ApiKey`
- `Email:Password` (SMTP)
